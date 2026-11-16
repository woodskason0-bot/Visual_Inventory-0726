# VIS — Code Notes

The reasoning that used to sit in long comments inside the code. I moved it here so
the code reads short and the "why" is still written down somewhere. Organized by
file, in the order the folders appear in the project.

**The rule for comments in the code, from 2026-11-11 on:**

- A comment stays in the code only when it says something the code can't: a
  constraint, an invariant, or a trap. Two lines at most on a member, three on a type.
- Terse, label style: the fewest words that still carry the rule. Fragments are
  fine. `// Standard: register, order. Engineer: change, scrap.`
- No history in the code. Pass numbers, dates, bug ids and "this used to" belong in
  git, `Commit_History.md` and the handoff's Pass log.
- No banner lines (`// =====`), and no comment that repeats what the next line does.
- In a view, every comment is a Razor comment, `@* *@`, inside `<script>` and
  `<style>` blocks too. Razor strips it on the server, so it never reaches the
  browser. `<!-- -->`, `//` and `/* */` in a view are sent to everyone and readable
  with View Source. `site.js` and `site.css` are served as files and can't use it.
- Anything longer goes in this file, under the file it describes.

**Status:** every file is done, in the terse voice I picked on 2026-11-11:
all the C#, `wwwroot/js/site.js`, `wwwroot/css/site.css` and every view, the last being
`Views/Home/_ModifyStockPartial.cshtml` on 2026-11-12. Comment lines in the source went
from 4,607 to 1,537 (measured again 2026-11-16), and since 2026-11-15 no comment in a
view reaches the browser. The sections below are in folder
order, `Program.cs` then Controllers, Data, Models, Services, Views and wwwroot, one per
file. A file that was done in parts has its notes in one section, in the order they
were written.

---

## Program.cs

- **Session cookie name.** VIS uses `.VisualInventory.Session` instead of the default
  `.AspNetCore.Session`. Browsers scope cookies by host, not by port, so when the
  Sourcing Tool runs on `:5001` and VIS on `:5000` on the same machine, the default
  name made them overwrite each other's session and log "Error unprotecting the
  session cookie."
- **First-run seeding.** Email routing and the starter roster are only written when
  their tables are empty. The roster list in `Program.cs` is the one to edit for a
  fresh database. A fresh database has to seed at least one Admin: levels can only be
  changed from inside the app, so an all-Viewer roster would leave nobody able to
  raise anyone.
- **Pickup alerts for the Standard tier.** Standard used to mean "gets pickup alerts"
  automatically, back when alerts followed the access-level band. Alerts are an
  individual opt-in now, so anyone seeded at Standard gets a subscription row, and a
  backfill at every start gives one to any Standard user who has none. It only
  inserts where a row is missing, so it can't create duplicates and can't switch
  alerts back on for someone who turned them off on purpose.
- **Vocabulary caches.** `OrgStructure` (Branches and Lines) and `LocationCodec`
  (Locations) are static classes read from all over the app, with no database access
  of their own. They're loaded once at boot and again after any Settings edit
  (`SettingsController.RefreshOrgStructure` / `RefreshLocationCodec`). Without the
  boot load, every fresh start would run on the compiled-in seed values until someone
  happened to save something in Settings.
- **Delivery photos.** They live in `C:\VIS_Image-Uploads`, outside the publish
  folder, for the same reason the database does: a republish must never touch them.
  They're still served over a normal URL so the app can show them.
- **Middleware order.** Session has to sit between Routing and Authorization.

## Controllers/HomeController.cs

- **Two pages where there was one.** "/" renders Command Center: the KPI strip, the
  map and location list, the Activity Feed, the Need PN and Need Serial donuts, Quick
  Actions, and Stock Alerts, Pending and Incoming Shipments. Advanced Filters, Omni
  Search, the results grid, the Export Wizard, the Compressor and Motor Registry and
  Modify Stock live on Search Center. `PopulateSearchViewBag` feeds both, so the JSON
  shaping isn't kept in step by hand in two places.
- **Need Serial counts against quantity.** It's real on-hand compressor stock with no
  serial recorded, the same "physical gap" framing Need PN uses. Counting blank
  serials among CompressorUnit rows gave nearly zero by construction: most stock has
  never had a row, and whoever creates a row tends to fill the serial in. Need PN
  said 100% and Need Serial said 0% right next to it. Checked against the real
  database the gap was 849 minus 184, which is 665, or 78%.
- **One definition of low stock, out of stock and needs-a-PN.** Search Center's
  stock views and Command Center's cards share the same helpers so the definition
  can't drift into another hand-typed copy. `GetLowStockItems` deliberately includes
  zero-quantity items, as the Stock Alerts widget always has. The KPI strip's Low
  Stock count excludes zero so it doesn't double-count with Out of Stock, and stays
  its own expression.
- **Type and Brand filter options** come from the items this viewer can see. Type and
  Brand are free text in the schema but a closed set in practice (14 types and 21
  brands across 492 items when this was written), so the filter offers what exists
  instead of asking someone to guess a spelling, and doesn't advertise a brand on a
  Line they can't reach.
- **itemsList stays Line-scoped, on purpose.** That's right for Add to Cart and
  Handle Stock, and `AddItem` has no Line check of its own, so widening the list
  would widen what can be ordered. Request Transfer needs the other Line's item,
  which Search now returns, so it gets a separate, narrower list holding exactly
  what the search returned.
- **The Item Card is fetched, not baked in.** It's returned as a partial and injected
  into the page's modal shell, so cart state and pending orders are current. The
  item lookup is unfiltered by design, since a search row can already show the same
  item. The activity list stays Line-scoped.
- **SmartRedirect** exists because a second page can trigger the same cart and stock
  actions the dashboard could. A hard-coded "back to Index" stranded Search Center
  users on the dashboard after every action.
- **Why ordering is Standard.** Interns come in as Standard and could add stock to
  items that already existed but could neither create one nor request anything, a
  half-capability that made no sense. Scrap, Ownership, Edit Details and thresholds
  stay at Engineer: changing or destroying what exists is a different kind of trust
  than adding to it. Add and Adjustment are Standard because runners restock and fix
  counts on a recount. Location Transfer changes no quantity, so it's Standard too.
- **The bulk default threshold** is Management and up, not Engineers. A manager with
  teams sets it for those teams' items; a manager with none sets it for every item.
- **Edit Details never changes Line.** Its Branch and Line fields moved to the
  internal transfer pane. The action passes null for the Line so a stale value posted
  from the hidden transfer pane can't reassign an item's Line while someone edits its
  part number.
- **Registration rules.** A Rheem part number is required on new registrations, and
  one that already belongs to another item is rejected so the same physical part
  can't be registered twice. Line is required too: older items with a blank Line
  still fail open, but nothing new can be registered without an owner. Team is
  optional and no longer defaults to Samurai.
- **The item id is assigned by the server.** The form's id box is a read-only
  preview. The real id comes from the Group and Type at save time, so a stale preview
  or two registrations at once can't collide. Group is derived from the Line picked
  on the form, not the registrant's own Line, and never changes afterwards.
- **Location encoding at registration.** The form posts friendly names. The
  controller converts them to codes, stores the code in the location columns, and
  rebuilds the FDA string from the codes, so a value supplied by the client can
  never put bad data in the database.
- **Three projections of one Locations table.** `BuildLocationTree` is keyed by
  name, for Registration and Intake (the client derives codes with its own copy of
  the rule, so the shapes have to match). `BuildLocationHierarchyCoded` is keyed by
  code, for the Modify Stock and Location Transfer cascade, since the stock columns
  store codes. `BuildLocationFilterRows` is flat with both code and name at every
  level, for Search Center's location filter.
- **Why the filter rows are flat.** The filter needs both halves at once: the code is
  what's stored and posted, the name is what a person reads. Flat rows let the three
  selects filter each other with data attributes and no dependency on the Modify
  Stock partial's `locHierarchyData`. That constant is declared at the partial's top
  level, so declaring it again on the page would be a duplicate `const` in shared
  scope, a SyntaxError that kills the whole script block.
- **The coded tree is built from the table, not from stock.** It used to be built by
  walking the codes already stored on items, so a Sub added in Settings couldn't be
  picked as a transfer destination until something was already stored there.
- **Rack and Row suggestions are not a vocabulary.** Rack and Row stay free-form
  values the teams assign. The map only surfaces what's already been typed on real
  stock at a given place, so picking one is a shortcut and a brand new value still
  types in fine.
- **Intake can't invent a location, and isn't blocked by a missing one.** The pickers
  only offer what exists. The escape hatch is "Not listed", which holds the batch for
  the superuser to map or create.
- **The "Not listed" sentinel.** The Parent select's "Not listed..." option posts the
  literal `__NEW__`, not blank. Checking only for blank meant the hold could never
  trigger through the real page, and `__NEW__` went through to the commit and was
  stored as if it were a location code (CCR-0255). Both intake actions now treat the
  sentinel as unresolved.
- **The Type gate on intake.** A row's type has to match an existing type, which is
  then stored in its existing spelling, or be explicitly flagged "Add as a new type".
  Anything else refuses the whole batch. A free-typed miss like "Contro" used to
  become its own type and mint its own id prefix. There's no Types table: the
  vocabulary is whatever real items carry, with one definition for the dropdown and
  the gate so the two can't disagree.
- **Line is required on intake** and is checked before the hold path, so a batch can
  never sit in the approval queue with no owner.
- **A partial import can be resubmitted.** The commit saves per row, so rows without
  errors did land. Resubmitting the whole batch is safe because rows already in are
  skipped.
- **Row-indexed form fields.** Extra serials, TC counts and the stock-update rows are
  posted with the row index in the field name, because array parameter binding can't
  key by row. The same flat prefix scan is used by every multi-row post in the app.
- **Return picker labels show Rack and Row.** Without them every Lean-To variant read
  as plain "Plant Test Cells" and two stacks of one item at different racks couldn't
  be told apart.
- **The return picker only offers the line's own team's shelves**, the same pool
  Pickup Queue narrows to and `ReturnLoan` enforces, so the picker doesn't invite a
  return that hands another team the units.
- **Serial pickers only name units that have a serial.** A unit can sit On Hand with
  only a lab number or nothing. As a named option it rendered as
  `<option value="null">SN: null</option>`, and the staggered default even opened on
  it, so a picker who touched nothing posted the literal serial "null". That missed
  the real anonymous unit and minted a bogus Picked Up row beside it, the CCR-0213
  orphan again. Anonymous stock is what the "No serial" option is for: the service
  then takes the oldest unnamed unit on that shelf.
- **Holds and the serial pickers.** A serial named in someone else's live hold isn't
  offered at all, since the pickup would refuse it. The orderer's own held units come
  first so the default picks are the ones their hold is for.
- **Pickup Queue lists only Pending lines**, the same set a pickup acts on. A
  deliberate partial pickup leaves its Split line on an order that stays Pending for
  another line; listing it offered units that had already left and judged the order
  against them.
- **Pickup Queue offers the same pool the pull uses.** Locations and serials are
  scoped to the line's team exactly as `FulfillOrderItem` scopes the real pull, or
  the picker could choose a spot the pickup then can't honour.
- **Awaiting Approval only shows what the viewer can approve.** It's filtered by Line
  and, once an item is split, by Team. Otherwise a same-Line Engineer on the wrong
  team would see a request that approval then refuses.
- **Store tasks.** Pinning one is Engineer and up, with a title and an optional note
  and no structured item fields. Claiming is Standard and up and the first claim
  wins. Only the person who claimed a task can finish it, and finished tasks drop off
  the board. Deliveries follow the same claim rules.
- **The delivery routing rule.** A named recipient's delivery also reaches the
  Engineers on their Line; failing that, on their Branch's Lines; failing that,
  nobody else. `AudienceLinesFor` answers "who does this recipient reach" when the
  delivery is logged, and `RecipientsReaching` answers "whose deliveries reach me"
  when the board is built. They're inverses and have to stay that way: if they
  disagree, a notification points at a board that doesn't show the delivery.
- **Why the Deliveries board is Engineer and up.** The Unknown bucket notifies
  exactly the Engineer level, and a notification pointing at a page the recipient is
  bounced off is worse than not sending it. So the board, Claim and Complete came
  down to Engineer with it. They stay open upward on purpose: Management and Admin
  aren't pinged any more, but shutting them out would strand a delivery addressed to
  a named manager. "Who can pick up an unlabelled box" and "who can a box be assigned
  to by name" are two different questions.
- **Unknown deliveries notify exactly Engineers.** Handling an unlabelled box is an
  Engineer job. It used to go to Management and up, which pinged 15 people and no
  Engineers at all. It isn't line-aware, because an unlabelled box has no Line.
- **An Engineer's board includes their Line's deliveries.** Without that clause the
  fan-out notified 13 people and landed them on an empty board. Only Engineers get
  it: Management and Admin aren't part of any Line's audience.
- **Who a delivery can be addressed to.** Anyone Engineer and up. Naming a manager
  stopped meaning "you go deal with this" once a named delivery also reached that
  person's Line. Viewer and Standard stay out because they can't open the board, and
  a box addressed to someone who can't see it is stranded. The access level is sent
  with the list so the view can group it: 44 flat names is a scroll, three labelled
  sections is a glance.
- **Two wordings for one delivery.** The recipient is told "a delivery was logged for
  you". The Engineers on their Line get it in the third person, because 13 people
  reading "logged for you" would each think it was theirs. The logger and the
  recipient are both left out of the Line message.
- **The pickup form's serial values.** Each unit's serial field posts a real on-hand
  serial, `__NONE__` when the picker says the unit has none, or `__ADD__` when they're
  naming a serial the roster doesn't know, typed into a second field. Lab number and
  serial are both optional and a blank posts through as null.
- **MaxUnitsPerLine.** The per-unit form loops run on posted numbers before the
  service gets to validate them, and the list is padded out to the posted index, so
  an absurd index would allocate until the process fell over. The cap of 5000 is far
  above any real order line; the whole Lean-To holds 636 compressors.
- **Short pulls at the page level.** Lines that came up short left stock untouched
  while the other lines picked up normally. The shortfall is stashed in TempData so
  Pickup Queue can render the "what's actually here?" form. When a correction is
  refused, everything was rolled back and the line is still flagged short, so the
  action puts the same form back; before that the order was stranded.
- **Logging serials from the roster is Standard on purpose.** Anyone passing a shelf
  who notices an unrecorded unit, or a typo, should be able to fix it on the spot.
- **Order Details shows the units a line pulled.** The view never fetched
  CompressorUnits, so a compressor line's serials were invisible on the order even
  though they're logged at pickup. They're grouped by order line, not by order, so a
  multi-line order doesn't mix serials across lines.
- **Sign-in matches the roster without regard to case.** SQLite compares text as
  binary by default, so "DeCory.Thomas" and "Decory.Thomas" are different strings.
  The normalised form flattens interior capitals, which real names have (DeCory,
  McDonald, O'Brien), so it missed its own roster row and that person fell through
  to Viewer on every sign-in. The normalisation is now only a fallback for a name
  that isn't on the roster.
- **The roster's spelling becomes the session name.** Notifications, Deliveries and
  the team membership join all match on that string, so a session name that merely
  looks right isn't good enough. It has to be the stored one.

## Controllers/ReservationsController.cs

- **Who can do what.** Every signed-in user can read Reserved Items; the global
  `RequireNameFilter` covers it. Reserving, editing and releasing are Standard and up
  on the action, and checked again per item and per reservation in
  `ReservationService`. The page layout is section 6 of
  `docs/Unit_Reservations_Spec.md`.
- **How the page is ordered.** Your own holds come first, one group per reason. Then
  your Line, the rest of your Branch, items with no Line, and other branches greyed
  out: less important, still readable. Inside a group it's soonest end date first,
  until-notice last, then oldest first.
- **After a save or release** the user goes back to the page they came from, always
  as a local path. From Search Center the Item Card reopens on the item just changed,
  so the result is right there.

## Controllers/SettingsController.cs

- **What Settings is for.** It's the one screen that replaces opening the SQLite file
  and editing a row by hand. It's gated by `[RequireSuperuser]`: the session name has
  to match the configured superuser and the session has to have entered the passcode.
  Every change writes a log row, the same convention as Edit Details.
- **Hidden things stay listed.** Teams, Locations, Branches and Lines are shown
  whether active or not, because Settings has to see a hidden one to bring it back.
  The usage counts next to Hide are there so nobody hides a team or a Line without
  seeing what still references it.
- **Whole-Branch assignment** posts the sentinel `__WHOLE_BRANCH__` in the Line field
  with the branch alongside. A user has a Line or a Branch, never both.
- **Adding a user at Standard** switches pickup alerts on for them, because Standard
  used to mean alerts came along for free.
- **Deleting a user is a hard delete and it's safe.** Logs, orders and tasks snapshot
  the person's name as plain text and not a foreign key, so history is unaffected.
  Their notification subscriptions cascade with the row.
- **Teams can't be renamed, on purpose.** Items store the team name as a plain string,
  like Line, so a rename would orphan every item pointing at the old value unless it
  cascaded, and a cascading rename is hard to undo. Add a new team and hide the old
  one; the history stays readable either way.
- **Approving a held intake** takes one of two shapes: map the request onto a
  location that already exists under another name, or create the location. Then the
  same commit path a known-location batch takes is run. The location chain is walked
  upward so the batch lands at the right depth: mapping onto a Sub has to fill in its
  Major and Parent too, or the item files itself at the wrong level.
- **A failed approval can be run again.** `CommitIntake` saves per row, so rows
  without errors did import even though the batch stays Pending. Approving again only
  imports the failed rows; the ones already in are skipped, not duplicated.
- **The map-zone reminder** only shows for a newly created Parent. Majors and Subs
  are reached through their Parent's menu, and mapping onto an existing location
  isn't the moment to remind anyone about a zone that was already there or not.
- **Rejected intake rows are kept.** A rejected batch is still a record of what
  somebody counted, and deleting it means typing it all again if the rejection turns
  out to be a misunderstanding.
- **A location name can't share a code with another.** Codes are derived from names,
  so two names that encode the same make `Decode` ambiguous: the first one wins and
  the second is invisible on every breadcrumb. Add Location and the map editor's
  new-Parent path both refuse it the same way.
- **Map zones.** Coordinates arrive from the drag editor as fractions of the image,
  never pixels, which is what lets a rectangle survive the floorplan being
  re-exported at another size. The geometry is checked before anything is created, so
  a bad drag can't leave a new Parent behind with no zone.
- **A Parent can be created from the map editor.** Drawing a box round an area that
  isn't registered yet is the normal case when a new team turns up, and bouncing to
  the Locations panel and back for it is friction with no purpose.
- **Zone overlap is warned, not blocked.** A small shared edge between neighbouring
  areas is normal. An image map sends a click to whichever area comes first, so a
  real overlap makes one of them unreachable, which is worth knowing without being
  stopped.
- **Hiding a location** also drops its name from the decode map, so anything still
  stored at that code goes back to showing the raw code until it's moved. The toast
  says so.
- **A team's project code is display and reporting only.** Nothing keys off it, so
  changing it doesn't touch items already carrying the old value.
- **A team's home Line is metadata.** It feeds the auto-fill on the registration and
  ownership forms and the compressor filter's suggestion. Visibility is still the
  Line on the item and the user.
- **Hiding a team counts both kinds of claim.** A team can own a slice of an item
  without being its family-level Team, so counting items alone reported "0 still
  reference it" for a team that held real stock.
- **Team membership is edited from the team's side.** Pick a team, tick who's on it,
  and Save replaces that team's whole member set in one diff. Team is still a label,
  not a visibility gate.
- **Branch and Line writes refresh the cache** so a change is live immediately and
  not after the next restart. A hidden Branch takes its Lines out of the pickers with
  it, even though the Line rows themselves stay active underneath.

## Controllers/UnitTrackerController.cs

- **What the page is.** A plain list of whole AC units (model, Package / Split,
  serial, sample, Line and Team, notes) that people can fill in while the full
  tracker is scoped. Units can't be ordered and aren't counted anywhere else in VIS.
- **Permissions.** Add is Standard and up. Update of model, package, serial, sample
  and notes is Standard and up; an Update that changes Line or Team is Engineer and
  up, checked inside the service. UpdateNotes is open to any signed-in user, Viewer
  included, and takes only the id and the notes so nothing else can change through
  it. The Where and Build boxes are Standard and up. Delete is Engineer and up. None
  of it relies on hidden buttons.
- **Posted or not posted.** MVC folds a posted blank to null, so "left alone" (a
  disabled select posts nothing) and "set to blank" can only be told apart by whether
  the field is in the form. Update checks the form keys for Line, Team and Package.
- **The row boxes answer JSON.** They save on click, and a redirect would reload a
  long list and lose the user's place. The request carries `X-Requested-With` so
  `[RequireLevel]` answers a refusal with a 403 the page can read.
- **The Edit modal's data** is serialized into the page so a click costs no second
  request. `JsonSerializer` escapes `<`, which is what keeps free text inert inside
  the page's script block.
- **Save and add another** keeps Model, Package / Split, Line and Team for the next
  unit. Serial, Sample and Notes start clean because they're what differs between
  units.
- **Toast text is capped at 400 characters** so a hostile raw POST can't push the
  TempData cookie past its size limit. Only the toast is capped; the stored value
  and the log row keep exactly what was typed.

## Data/AppDbContext.cs

- **The index trap.** Every index a migration creates has to be declared here with
  `HasIndex`, and the block has to exist before `migrations add` is run. The
  migration creates the index, so the model snapshot declares it. If the runtime
  model has no matching `HasIndex`, the two disagree, EF raises
  `PendingModelChangesWarning`, and `Migrate()` aborts before applying any migration
  at all. That silently blocked three passes in a row. `CompressorUnit.ItemId`,
  `MotorUnit.ItemId`, `TrackedUnit.Line` and the Reservation indexes are all here for
  that reason as much as for speed.
- **Rheem part number** is a business identifier like `ItemId`, so no two items may
  share one. The index is partial so blanks and the shared "N/A" value can both
  repeat. Older blank rows were folded into "N/A".
- **Variant numbers are not unique** per item on purpose. Retired rows keep their
  number for history, and a freed number may be reused by a later variant.
- **Compressor serials** are unique per (ItemId, SerialNumber), not on their own. One
  model may not carry the same serial twice, but two different models may share one,
  because LG reuses serials across model lines. Blank is exempt because most units
  have no serial captured yet.
- **OrgLine names are unique across all branches**, because `User.Line`,
  `InventoryItem.Line` and `Team.Line` are flat strings with no branch in them. Two
  branches sharing a Line name would make the reverse lookup ambiguous.
- **Transfer requests** are indexed by requester and by item. Awaiting Approval
  reads everything against an item and then filters to the viewer's Line in memory,
  the same way the compressor roster does.
- **Location zones cascade** with their Parent. A zone pointing at nothing would
  draw an unclickable ghost on the map.

## Models/AppSetting.cs

- **Why a key/value table.** App-wide switches used to mean opening the SQLite file
  by hand. A setting is a row added from the Settings page, so one more switch never
  needs a migration. Values are stored as text and parsed by whoever reads them.

## Models/Branch.cs

- **Branch and OrgLine replaced a hard-coded dictionary** (`OrgStructure.BranchLines`).
  `OrgStructure` is still the read path everything calls (`BranchFor`, `GroupFor`,
  `IsValidLine`). It's refreshed from these two tables at startup and after any
  Settings edit (`OrgStructure.Refresh`, `SettingsController.RefreshOrgStructure`).
- **IsActive** is the same soft hide as Team and Location: the branch drops out of
  the pickers without orphaning items or users that already carry one of its Lines.

## Models/CompressorUnit.cs

- **A roster, not a pickup log.** One row per physical compressor. Rows were first
  created only at pickup, which left stock on a shelf with nowhere to live. Since
  Pass 6A a unit is recorded when it is known (counted in, or captured at pickup)
  and annotated as it moves.
- **Quantity stays the count of record.** `ItemVariant.Quantity` is the truth about
  how much stock exists. Unit rows are a partial overlay that fills in over time: at
  the 7/28 intake only 183 of 825 compressors had a serial. Unit rows must never
  drive counts, and a pull with no matching unit row is normal, not an error.
- **Serial and lab number** are both permanent and both belong to the unit: the
  manufacturer's serial and Rheem's own asset tag for the same machine. VIS records
  lab numbers and never assigns them, until a numbering process exists that covers
  other item types too. That's also why lab number has no uniqueness rule.
- **Serial uniqueness is (ItemId, SerialNumber)**, not serial alone. LG reuses
  serials across models.
- **ItemVariantId is nullable** because a unit that's been picked up and not returned
  isn't on any shelf. It's set again on return. Without it, "where is this exact
  compressor" can't be answered for a model stocked in two places (18 models were, at
  the 7/28 intake).
- **OrderId** was NOT NULL before Pass 6A, which is what blocked counted-in stock.
  It's null while the unit is On Hand.
- **TransferRequestId** is set instead of `OrderId` / `OrderItemId` when the unit
  left by an internal transfer approval rather than an order pickup. Never both.
- **ReservationId** names the reservation an On Hand unit is pinned to. The unit
  counts inside that reservation's quantity on its stack. It only means something
  while the reservation is live, so it's read through `ReservationService`, never on
  its own, and it's cleared when the unit leaves or the hold ends.
- **UnitStatus** values are plain strings, the same loose-string style as action
  types and Line.

## Models/Delivery.cs

- **What it is.** One logged incoming delivery: one photo plus whatever is on the
  label, routed to whoever should act on it. The claim lifecycle (Open, Claimed,
  Done; first to claim locks their name in, everyone else sees "claimed by X") is
  copied from `VisTask`.
- **The four label fields** (tracking number, order number, shipping brand, item
  brand) use the same N/A toggle as the Rheem part number: blank and the literal
  "N/A" are treated as the same thing everywhere they're checked.
- **Recipient** is either one specific Management+ user or `UnknownRecipient`, the
  shared bucket for "don't know whose this is", which fans out to every Management+
  user. The logger always picks one or the other.

## Models/IntakeBatch.cs

- **Only held batches live here.** A bulk intake whose location already exists
  commits straight through. This table holds the ones waiting on a decision, which
  keeps the queue meaningful instead of a log of everything.
- **Why hold at all.** A team entering stock can't invent a location, but isn't
  stopped by a missing one either. They type what they call it, the rows are kept
  intact, and the superuser either maps it to a place that already exists under
  another name or creates it. The same commit path runs afterwards either way.
- **RequestedLocation is kept verbatim.** If three teams all write "North Bay" for
  the same place, that's worth seeing before deciding what the real name should be.
- **Serials** (compressors) is comma-joined, so a row can name more than one unit.
  Read it with a plain `Split(',')`.
- **ThermocoupledQty** (motors) is a count, not per-unit identity.

## Models/InventoryItem.cs

- **Family and stacks.** `InventoryItem` is the family record (`CCR-0001`). It holds
  what is true no matter where the stock sits: identity, ownership, and the alert
  threshold, which is an item total by design. The physical stock lives in
  `ItemVariant` rows, one per location.
- **The pass-through properties.** `Quantity`, `Parent`, `Major`, `Sub`, `Rack`, `Row`
  and `FdaString` on the item are computed over its active variants. Reads resolve
  against the primary variant (the lowest active number), or the sum of active
  variants for `Quantity`. Writes land in staging fields that `CreateItem` uses to
  build variant 1. They are `[NotMapped]`, so they can't be used inside an EF query
  (`Where`, `Select`, `Sum`): EF can't translate them. Query `ItemVariants` directly.
  `TotalThermocoupled` is the same kind of thing, a display convenience.
- **Rheem part number.** Every item should carry the part number off the physical
  label. It's family-level like `ItemId`: it's on the binder, not the pockets. Blank
  means not captured yet (older rows). Non-blank values are unique across items
  through a partial index that exempts blanks.
- **RegisteredAt** is nullable so existing rows migrated cleanly; they were
  backfilled from their earliest "New Registry" log.
- **Line.** One of `OrgStructure.AllLines`. Line replaced Team, Group and Project
  Code as the thing that decides who can see an item. Those three are still there
  and still feed id generation and the old email routing, but they don't gate
  anything. A blank Line fails open (visible to everyone) until an Engineer or above
  assigns one in Edit Details.

## Models/ItemVariant.cs

- **What a variant is.** One physical stack of a family at one location, numbered
  `-01`, `-02` and so on, with its own location, quantity and FDA string. Variant 1
  is the original.
- **Retired, not deleted.** When a variant's stock is fully merged into another, it's
  marked `IsRetired` instead of deleted, so old logs and orders that point at it
  still resolve to a real row. A retired number can be reused by a new variant, which
  is exactly why logs snapshot their details at write time.
- **Team on a variant.** This is per-team quantity ownership (scoped 2026-08-26). A
  variant's Team defaults to its item's Team, so every single-team item behaves the
  way it always did. A second team's share of the same item is a second variant with
  a different Team, not a new table, and nothing requires it to sit at a different
  physical location. Plain string, same convention as Team and Line everywhere else:
  no foreign key, must be an active team.
- **ThermocoupledQty** is a marked subset of `Quantity`, not a separate pile. The
  invariant is `0 <= ThermocoupledQty <= Quantity`, always. Only motors use it;
  everything else leaves it at 0 so the invariant holds for every row.
- **Location fields.** Parent, Major and Sub are stored as codes (see
  `LocationCodec`). Rack and Row are free text the teams assign. `FdaString` is the
  joined code path.

## Models/LedgerDraft.cs

- **A cart, not a table.** `LedgerDraft` lives in the session, so its fields need no
  migration. New fields are added nullable or with a default so a draft already
  sitting in someone's session still deserializes.
- **RequestedTeam** is only the answer to the ambiguous case: the requester is on
  more than one team and the item's variants span more than one. Everywhere else
  it's null and `Submit` resolves `OrderItem.Team` from the requester's own team.

## Models/Location.cs

- **Why the table exists.** The location vocabulary used to be four hand-kept
  copies: `LocationCodec.CanonicalNames`, a JS `locMap`, a hard-coded option list,
  and the same list again on My Orders. They had drifted: 34 `ItemVariant` rows
  carried Sub codes MZA3 to MZA8 that no copy knew, so the UI showed raw codes at
  every breadcrumb.
- **Codes are never stored.** `LocationCodec.Encode` derives the code from the name
  by the 1st/3rd/5th/last rule, and that rule is the only authority. Storing a code
  would create a second one that could disagree with it.
- **Rack and Row stay free text** on `ItemVariant`. They're per-shelf detail, not
  shared vocabulary, and the Lean-To lives there by design.
- **ParentId** gives the table the same shape the JS cascade needs, so it's
  serialized straight out of this table.

## Models/LocationZone.cs

- **Why the table exists.** The map zones were four hard-coded `<area>` tags: a
  fifth copy of the location vocabulary, missed because it was an image map and not
  a dropdown. A Parent added in Settings showed up in every picker and was
  invisible on the map.
- **Its own table, not four columns on Location**, because one Parent can occupy
  more than one part of the building.
- **Fractions, not pixels.** The old markup stored pixels measured against a
  1146x643 image, which tied the data to that exact file: swap the floorplan for
  another resolution and every zone shifts. "44% across, 57% down" survives it.
- **Only Parents get zones.** Majors and Subs are reached through the menu that
  opens after the click, which is already driven by live stock data.

## Models/MotorUnit.cs

- **CompressorUnit's sibling.** Same roster shape and lifecycle. It's a separate
  table and not a generalized `CompressorUnit` because motors have no serial number
  to key uniqueness on.
- **TC only.** Thermocoupling comes from the manufacturer; VIS doesn't do it. So
  only the TC subset is worth tracking per unit, and a plain motor never gets a row.
  `ItemVariant.ThermocoupledQty` stays the count of record, the same way `Quantity`
  does for compressors.
- **Lab number has no uniqueness rule.** It's the only identifying field and it's
  often blank, so two units without one would collide.

## Models/MyActivityViewModel.cs

- **The page was "My Orders"** until it grew past orders. Loans and internal
  transfers live on it too.
- **Compressor lines** are gated behind "Done Using" instead of showing Return and
  Scrap up front, because compressors are normally consumed, not returned.
  Outstanding on those lines reads as "not yet dispositioned", not "expected back".
- **Units is usually shorter than Outstanding.** Only some compressors have a serial
  on record (184 of 825 when this was written), so a line of 3 may list 1 unit and
  leave 2 unidentified. The view shows the remainder as a plain quantity, the way
  motor loans already work.

## Models/NotificationSubscription.cs

- **A real foreign key, on purpose.** Logs, orders and tasks snapshot names as
  plain strings so history survives a deleted user. A subscription is current-state
  config, not an audit record, so it's keyed to the `User` row and cascades on
  delete.

## Models/Order.cs

- **SplitFromOrderId** is set when this order exists because a line on an earlier
  order didn't fully ship there. Either the picker capped the pickup and deferred
  the rest (the original line reads `Split`), or `ReportShortPull`'s shelf-count
  correction reissued the real quantity. It's a loose reference, only there to show
  the lineage in Order History.

## Models/OrderItem.cs

- **Status is per line** so one short line on a multi-item order can be pulled aside
  for a stock correction while the other lines pick up normally.
  - A line that comes up short at pickup is set to `Cancelled`. Its real fulfilment
    happens on a freshly issued order against the corrected count, rather than
    letting the order close short.
  - `OrderService.ReportShortPull` flips a `Cancelled` line to `Corrected` the moment
    it starts acting on it, so a double submit can't apply the same correction twice
    or reissue a duplicate order.
  - `Split` is a different situation and is kept separate from `Corrected` on
    purpose. The shelf had enough, or the picker never found out otherwise: the
    picker chose to take what was at hand and defer the rest, which becomes a real
    Pending order (`Order.SplitFromOrderId`) instead of an unaccounted remainder.
- **ThermocoupledCount** is capped at what's available when the line is added to the
  cart, and rides the order so pickup pulls exactly that many TC units.
- **LoanOutstanding** covers TC motors and Controls only. It's set at pickup and
  drawn down by Return and Scrap on My Activity. The line's loan bench disappears
  when it reaches 0.
- **RequestedVariantId** is the stack the engineer asked for. Null means "either
  location" (the picker chooses) or an item with one location. It points at
  `ItemVariant.Id`, which is stable across renumbering, and variants are retired
  rather than deleted, so old orders always resolve.
- **Team** is resolved at Submit, not chosen freely. It defaults to the requester's
  own team and is only asked for when it's really ambiguous: the requester is on more
  than one team and the item's variants span more than one. Blank when the item has
  no team split or the requester has no team, which fails open like Line does. It
  limits the pickup pull to that team's own variants.

## Models/OrgLine.cs

- **Line names are unique across every branch**, not per branch. `User.Line`,
  `InventoryItem.Line` and `Team.Line` are flat strings with no branch qualifier, so
  two branches can't share a Line name. Those strings are deliberately not foreign
  keys to this table.

## Models/Reservation.cs

- **What a hold is.** "Reserved for (Reason) by (ReservedBy) from (StartDate) to
  (EndDate or until notice)". The spec is `docs/Unit_Reservations_Spec.md`. It's a
  hard hold: nobody but the reserver can order, pick up, transfer or scrap what it
  holds. It's counted per stack in `ReservationLines`, and specific compressor or TC
  motor units can be named through their `ReservationId`.
- **Available, never On hand.** The stock stays on the shelf and in
  `ItemVariant.Quantity` until it really leaves. `StockClaims` has the math.
- **Live.** Every query goes through `ReservationService.Live`, so an expired hold
  stops holding at midnight even before the sweep marks it Expired.
- **Reason is also the purpose group.** The Reserve dialog offers a person's open
  reasons back to them, and Reserved Items groups their holds by it.
- **StartDate is informational.** The hold starts when it's made.

## Models/Team.cs

- **Why the table exists.** Samurai and Ninja were hard-coded in four dropdowns and
  one ternary (`ProjectCode = newTeam == "ninja" ? "7165" : "7166"`), which would
  have quietly given every later team Samurai's project code.
- **A vocabulary table, not a foreign key.** `InventoryItem.Team` and `User.Team`
  stay plain strings, like Line. Hand-written SQL against the database stays simple,
  and an item whose team was later hidden still reads sensibly. The cost is that a
  rename doesn't cascade, which is why rename isn't offered (see
  `SettingsController`).
- **Team is optional.** Blank or "N/A" is a real choice. Team is a label for
  reporting. Visibility belongs to Line and nothing about Team touches it.
- **IsActive** is a soft hide, the same pattern as users: the team drops out of the
  pickers without orphaning the items that reference it.
- **Team.Line** is the team's home Line, or null. Branch isn't stored; it's derived
  with `OrgStructure.BranchFor(Line)`. It's pure metadata. It feeds two
  conveniences: picking the Team suggests the Line on the registration and ownership
  forms, and suggests the Branch and Line in the compressor filter. Both are
  suggestions a person can override.

## Models/TrackedUnit.cs

- **What it is.** One whole AC unit on the Unit Tracker's holdover list: a plain
  list people can fill in now while the full tracker (tag, decoding, statuses) is
  still being scoped. The full tracker extends this table later, so nothing typed
  here has to move.
- **Why it's called TrackedUnit** and not Unit: Unit collides with `CompressorUnit`,
  `MotorUnit` and `UnitStatus`.
- **No minted id.** The row's own `Id` is the reference (shown as `#12`) until the
  tag rule exists.
- **Branch isn't stored.** Line is what VIS stores everywhere, so Branch is derived
  for display with `OrgStructure.BranchFor(Line)`. Storing both would be a second
  field to keep in step.
- **Blank strings, not nulls.** Non-nullable strings default to `""`, like
  `InventoryItem`, so queries need no null checks. "Not recorded" is `""` for
  PackageType, Whereabouts and BuildStatus.
- **Where the rules live.** Lengths are on the model. Everything else (trimming,
  whether a Line or Team is valid, Package / Split) is enforced in
  `TrackedUnitService`. Model, serial and sample number are free text with no format
  rules; the model number is trimmed and keeps its case.
- **PackageType is optional** on purpose: the list gets filled from sheets that
  don't say.
- **Whereabouts** is deliberately not called Location or Status. The full tracker
  spec already uses Status (On Hand / Transferred Out / Scrapped) and Parent / Major
  / Sub for the real shelf location. This is a quick "yes, I know where it is" flag.
  One value at a time.
- **BuildStatus** uses the field name the full tracker spec uses, with "Built" where
  that spec said "Ready", so it carries over unchanged. Independent of Whereabouts.
- **The option lists** (`TrackedUnitPackage`, `TrackedUnitWhereabouts`,
  `TrackedUnitBuildStatus`) are plain strings in the same shape as `UnitStatus`. The
  page renders its boxes from `All`, so adding an option is one line.

## Models/TransactionLog.cs

- **ItemName is a fallback, not the source.** The feed's first lookup is a live join
  against `InventoryItems`, so a later rename shows up on old rows. The snapshot is
  only used when that join comes back empty, mostly for a deleted item. Logs written
  before the field existed have it blank and fall back to "Unknown item".

## Models/TransferRequest.cs

- **Parallel to Order, not built on it.** A transfer never touches the cart or order
  pipeline. An item can be ordered by its own Line and transferred to another at the
  same time; the only thing the two paths share is the `ItemVariant.Quantity` they
  both decrement.
- **One-way, not a loan.** The first design was a Borrow with a due date and a
  Return flow. In practice a transferred unit never comes back, the same way a
  picked-up compressor stays Picked Up. Only the request needs the lending Line's
  approval (Engineer and up, same Line or Team as the item).
- **RequestedVariantId is a preference.** If that stack can't cover the quantity by
  approval time, approval spills into the item's other active stacks the way a
  normal pickup does.

## Models/TransferViewModels.cs

- **AwaitingApproval is the queue itself.** A decided row needs no more action and
  drops off, the same convention the loan bench uses. MyRequests keeps full history
  because the outcome is still worth showing.
- **UnitSlots.** One entry per physical unit an approval would pull, in the exact
  order `ApproveTransfer` pulls them. Slot u offers the units on the shelf slot u
  comes off, instead of every location's serials pooled together. Pooling is what
  made a reasonable pick get refused by `AssignOneCompressorUnit`'s location-scoped
  match.
- **DefaultSerial is staggered** across the slots that share a stack: one real
  serial each while they last, then "No serial". Otherwise a multi-unit approval
  opens with every dropdown aimed at the same physical unit. The duplicate would be
  rejected anyway; not defaulting into the rejection is friendlier. Pickup Queue
  staggers its defaults for the same reason.

## Models/User.cs

- **A roster, not authentication.** It powers the Identify dropdown so people pick a
  consistent `First.Last` instead of free-typing.
- **A blank Line fails open** (sees everything) so nobody's dashboard looks broken
  before they've been assigned. It's set only from Settings.
- **Line or Branch, never both.** Branch is for someone who needs more than one Line
  without going all the way to Admin, like a director over a whole Branch. The
  Settings UI clears one when the other is set.

## Models/UserTeam.cs

- **Replaced the single `User.Team` string**, so a user can be on several teams.
  Membership is managed from the team-centric picker in Settings (pick a team, check
  the users on it), not from the user row.

## Models/VisTask.cs

- **No structured description, on purpose.** Supervisors often don't have item
  details when they post a task, so it's a title and an optional note pointing at
  the New Item Registry.
- **TaskType** is only ever "Store" today. It's a field so another kind of task can
  be added without a schema change.

## Models/ViewModels/ItemCardViewModel.cs

- **One place for the numbers.** Everything the Item Card shows is gathered
  server-side here, so the card never re-derives a number the rest of the app
  already has a definition for.

## Models/ViewModels/PendingOrderItemViewModel.cs

- **VariantChoiceViewModel.Quantity** is kept apart from `Label` (which folds the
  count into a display string) so the page can check client-side whether a location
  covers the line's full quantity.
- **OnHandUnits** is empty for non-compressor lines and for a location with nothing
  tracked yet. Untracked units are the norm, not the exception.

## Services/AccessLevels.cs

- **Tiers are plain integers** so every check reads as "level >= required", and
  changing a person's tier is a one-number change on their Users row.

## Services/CurrentUserService.cs

- **Not authentication.** It records the name the person picked so stock changes and
  pickups are attributed to a real person. It doesn't verify who they are.
- **Level, Line and Branch are copied in at sign-in**, then re-read from the
  person's roster row on every request by `RequireNameFilter`. A change made in
  Settings reaches an open session on its next click.

## Services/DeliveryPhotoStorage.cs

- **Outside the publish folder**, the same deliberate move as `inventory.db`, so a
  republish can never touch the photos. `Program.cs` maps `RootPath` onto
  `RequestPathPrefix` so a saved file is viewable without living in `wwwroot`.
- **System.Drawing.Common, not ImageSharp.** VIS is Windows-only by design already
  (self-contained win-x64 publish, absolute `C:\` paths), so there's no portability
  cost, and it avoids ImageSharp v4's commercial license for a closed-source app.
- **MaxDimension** keeps the photo folder from growing without bound. A label photo
  doesn't need phone-camera resolution to stay readable.

## Services/EmailService.cs

- **The stock alert fires once**, on the way down through the threshold: the old
  quantity was above it and the new one is at or below it. If the item was already
  below, the manager already knows. It goes to the supervisor of the item's team,
  with the manager and the group's other supervisors on CC.
- **No real mail server yet.** When the SMTP setting is blank or a placeholder, the
  message is printed to the console instead of sent, so nothing crashes on dummy
  settings.

## Services/InventoryService.cs

- **Line visibility is for browsing.** `ApplyLineVisibility` is applied to `GetAll`
  and `ExportToCsv`. It is not applied to `GetById`, `FindByRheemPart` or the quantity
  math, which need the whole-org picture so lookups, duplicate checks and an
  Engineer's cross-Line reassignment keep working. Admin sees everything. A user with
  a Branch and no Line sees every Line under that Branch. Blank fails open at every
  step: a blank item Line is visible to all, and a user with neither Line nor Branch
  sees everything, so nobody's dashboard looks broken mid-rollout.
- **Search is not Line-filtered, on purpose.** Search is the one surface where
  another Line's items are meant to show up, so someone can find one and request a
  transfer. The card loop calls `IsOwnLine` per item to decide between the normal
  buttons and Request Transfer.
- **IsOwnLine repeats the visibility rule instead of sharing it.** One has to stay an
  expression EF can turn into SQL; the other only ever runs against loaded objects.
  Folding them together would either break the translator or cost a round trip.
- **ResolveTransferTeam is the single definition** of which team's slice a transfer
  is aimed at. `CanApproveTransfer` gates on it, `ApproveTransfer` scopes its pull
  loop to it, and the approval UI builds its serial picklists from it. When those
  three disagreed, a Ninja-only Engineer approved a 1-unit Ninja slice and 4 of the
  units came out of Samurai's stack. Blank means no team boundary, which covers an
  unsplit item, no pull location named, or a variant with no team.
- **CanApproveTransfer has two gates.** Own Line, as for every transfer check. And
  once the item is really split across more than one team, team membership too: an
  Engineer on the right Line but the wrong team can't approve away a slice that isn't
  theirs. It builds the Awaiting My Approval queue and is enforced again server-side.
- **ResolveOrderingTeam** was pulled out so Submit and the Search Center card can't
  drift. Submit throws on an unanswered ambiguity; the card just wants a number and
  treats ambiguous as "no scope yet". A card promising "Available: 15" while Submit
  refuses at 12 is the same kind of disagreement as the transfer one. Ambiguous means
  more than one of the requester's own teams has a real claim on the item, not merely
  that the person is on several teams.
- **Log visibility** follows item visibility. View Logs and the Activity Feed used to
  show every action on every item, a wider audience than could see those items in
  Search. A log row is visible if it names no item (Settings and admin actions), if
  its item has been deleted (nothing left to check a Line against), or if the viewer
  can see the item.
- **Item ids count from the highest number ever used**, per prefix, not the highest
  on a live item. Delete Item removes the item row but leaves its logs, unit rows,
  completed order lines and decided transfers under the old id, and the Item Card,
  View Logs, My Activity and the rosters all read them back by that string. Counting
  live items only would hand a deleted top-numbered id, and its whole history, to the
  next registration with that prefix. Old ids that don't match `PREFIX-digits` are
  ignored so they can't poison the count.
- **CreateItem resets what the form never posts.** The action model-binds a whole
  `InventoryItem`, so a crafted post could arrive with its own Variants, Id,
  RegisteredAt or AlertThreshold. It also refuses a negative quantity itself, because
  `form.submit()` skips the form's `min="0"`.
- **Staged values are read before the variant is added.** Once a variant exists, the
  item's pass-through getters read from it and not from the staging fields.
- **Variant 1 inherits the family's Team.** Left blank, that stack would be invisible
  to team-scoped ordering and its own team couldn't order it.
- **Why location filters read columns.** `FdaString` isn't a consistent shape in real
  data. It comes in 0-, 1-, 2- and 4-dot forms ("NWES", "PATS.LEAN-TO",
  "ETRD.CNEA.CNE6", "RLB.MTOE.SMRI.RACK 8.Row 1") because older rows squeezed out
  empty levels instead of padding them with "0", and it has a case split too
  ("PATS.LEAN-TO" 185 times, "PATS.Lean-To" once). In "PATS.LEAN-TO" the second
  segment is a rack, not a Major. Prefix-matching that string silently misses rows.
  The Parent / Major / Sub columns have none of those problems.
- **One `Any()` carries every location level.** Chained `Any()` calls would let an
  item qualify by matching the Parent on one variant and the Major (or Rack) on a
  different one, which isn't "stock at this location" in any sense a person means.
- **The old `notes` filter is gone from Search.** Description joined the omni sweep
  and the FdaString half became the real location filter. The old name filter slot
  is the Rheem part number filter; item name is still reachable through omni.
- **ExportToCsv used to prefix-match FdaString** with one dot-joined string. The
  wizard dropped blank levels before joining, so picking a Parent and a Rack posted
  "RLB.RACK 8", which matched nothing and returned an empty CSV while 29 variants sat
  on that rack. Its team filter also matches a team that only owns a slice of an
  item, and the Team column lists every team with a claim on an active variant.
- **Availability goes through StockClaims.** Live holds and Pending lines on Pending
  orders are claims, and a person's own holds are available to them. The line has to
  be Pending as well as its order: a deliberate partial pickup marks its line Split
  (those units already left) while the order stays Pending for another line.
- **GetAvailableForOrder** only lets holds and orders made before this order compete
  with it, so a later order never blocks an earlier one.
- **DeleteItem leaves history alone.** Logs and already Returned or Scrapped unit
  rows keep reading sensibly under the old id, the same idea as the soft hide on
  teams and users. Re-registering the same model later mints a fresh id.
- **DeleteItem checks the loan on the order line.** `LoanOutstanding` is the
  authority. Controls never get unit rows, so the unit-row check can't see them;
  deleting a Control with units out left a loan nobody could return.
- **DeleteVariant covers the gap Delete Item leaves.** Delete Item needs the item's
  total quantity to be 0. A single stack can drain to 0 (a scrap, a location transfer
  that merges everything elsewhere) while the item still has stock in other places.
- **DeleteVariant and open transfers.** `ResolveTransferTeam` reads the requested
  variant to decide whose slice a transfer is aimed at, and a variant it can't find
  resolves to blank, which fails open. So deleting the stack a live request names
  wouldn't just orphan the request: it would turn a team-gated request into one any
  Engineer on the Line could approve out of any team's stock. It's refused.
- **Edit Details rules.** Rheem part number can be set or changed but is rejected if
  another item already owns it, because it's a business identifier like `ItemId`.
  Brand is fill-if-blank only: an existing brand is never overwritten from here, and
  the caller is told so. The lookup isn't Line-filtered, because reassigning
  ownership means finding and acting on an item outside your own Line.
- **Alert threshold logging.** The per-item change is logged only when the value
  really changed: the Alert Rules modal is opened to read a threshold as often as to
  set one, and Save fires either way. The bulk set logs even when nothing changed,
  because "Apply to all" is a confirmed overwrite of every threshold in scope, so the
  click is the event and "0 changed" is a real answer.
- **The three type rules live here and nowhere else.** Motor types end with "Motor"
  and are the only TC-eligible ones. Control types end with "Control" and loan their
  full quantity (9 items were typed "Control" at the 7/28 audit, so that path is
  live). Other control-like stock (EEV, VFD, Valve, TXV) isn't matched, because Type
  is free text and those aren't spelled that way. Compressor is an exact match on the
  literal the sidebar quick filter already uses.
- **The compressor roster** holds On Hand stock as well as units that left, so it's
  ordered by `RecordedAt`. `PickedUpAt` is null for anything still on a shelf, and
  ordering by it would sort every on-hand unit into one clump.
- **LogCompressorUnits** is the other entrance to the roster besides pickup. It lets
  anyone passing a shelf record a serial that was never picked up, which is how the
  serial gap closes over time. The page pads the roster with blank rows up to each
  variant's quantity, and most stay blank.
- **LogMotorUnits reaches Picked Up rows too.** A motor's lab number is worth
  capturing whenever someone notices it, since there's no serial to fall back on.
  Scrapped rows are final. A new row is still On Hand only: you can't invent a unit
  that's out on loan, only correct one pickup already created.
- **Bulk intake has one commit path.** A batch whose location exists runs it straight
  from the Intake screen; a held batch runs the same method once the superuser
  resolves the location. Two paths would have drifted apart within a month, which is
  why it lives in the service and not a controller.
- **Preview** runs every check and reports what would happen without writing. It
  does before the fact what the hand-written verify SQL files did after it.
- **Intake's Group comes from the batch's Line**, the same rule New Item Registry
  uses, so an item gets one id prefix whichever door it came through. It used to
  follow the submitter's own Line, which is blank for whole-Branch users and so
  always minted a "C".
- **SameShelf compares the five location columns.** The app writes `FdaString` in
  more than one shape ("PATS.Lean-To" from Registry and Intake, "PATS.0.0.LEAN-TO.0"
  from Modify Stock's new location, Location Transfer and Loan Return), so comparing
  that string missed the same physical shelf.
- **Intake to a known model adds a variant, never a second item.** That's the rule
  the compressor loads established, and why 18 models legitimately sit in two places.
  The new variant takes the existing item's Team and not the batch's: Intake has no
  per-variant team picker, so passing the batch's team through would split an item
  across teams as a side effect of an import.
- **The one thing that blocks an intake row** is a real part number already owned by
  another family, because the same physical part would then exist under two ids.
- **Serials given at intake are stock on the shelf**, so each unit is recorded On
  Hand, not Picked Up. A duplicate (item, serial) is skipped and not thrown: the
  unique index would reject it anyway, and a whole batch failing over one repeated
  serial helps nobody. Intake, New Item Registry and Modify Stock's Add all use the
  same entrance.
- **TC count at intake.** Motors have no serial, so intake takes a count. It mints
  that many blank MotorUnit rows, the same shape pickup creates for untracked TC
  stock, so a lab number has a real row to attach to later through Log Units.
- **CommitIntakeStockBatch reuses ModifyStock** for everything (existing stack or new
  location, TC clamps, serial logging, the log row). Its own logic is only working
  out which variant the batch's one shared location is for each item. It refuses an
  Adjustment at a location the item doesn't occupy: Adjustment has no "new location"
  idea in ModifyStock, so letting it through would adjust the item's primary variant
  and not the place the person was looking at. The first failure rolls back the whole
  batch, so a batch never half-applies.
- **LoanOutstanding means two things.** For a Control or a Motor it's a library book:
  expected back. A compressor is normally consumed, so there the same counter means
  "not yet dispositioned", nobody has pressed Done Using. Reusing the field was a
  choice: one counter that can't drift beats two kept in step across pickup, return
  and scrap. The My Activity page carries the wording.
- **Ticked units on a partial move.** The unticked rows have to fit in what stays.
  That minimum is capped at what's moving, because a stack already carrying more rows
  than units would otherwise be impossible to move at all.
- **RetireSurplusUnits.** Before it existed, a Scrap or downward Adjustment lowered
  the count and the roster kept the serials. Phantom units were offered at pickup,
  shown on the Item Card, and blocked Delete Stack for good. Taking the whole stack
  (or all its TC) takes every row with no question asked. Anything short of that
  needs the person to say which recorded units are gone. A unit named in a live hold
  can't be ticked as leaving; a short-pull correction can take it because that's a
  recount and the holds shrink to fit afterwards.
- **Add and Scrap take a positive amount.** A negative Scrap used to pass through the
  clamp and add stock (and a negative Add subtracted it), logged as the opposite of
  what happened. Only Adjustment is signed.
- **Scrap and Adjustment clamp and say so.** A scrap past what's on the shelf clamps
  to what's there, and the log (and the CSV's ScrappedQty, which sums those logs)
  records the clamped amount. A downward Adjustment floors at 0 the same way.
- **Adjustment can reclassify.** "Take my existing N and mark X of them as TC" is a
  different action from Add's "these new units include X TC". The TC number on an
  Adjustment is added to what's already on the shelf and isn't tied to the quantity,
  which can be 0.
- **Ownership means Line.** The old "New Group" picker offered Commercial,
  Residential and International: two Branches and one Line, flattened as peers. Group
  is derived from the registering user's Line and frozen at creation because it mints
  the id prefix, so a transfer must not move it or the item's Group would contradict
  its own id. `newGroup` is still in the signature and is ignored.
- **An unknown Line on Ownership throws.** It used to return the success-shaped
  result with nothing changed, logged or saved, and the page toasted "applied" over a
  no-op.
- **Project code travels with the team row.** It used to be a ternary that gave every
  team that wasn't Ninja Samurai's code.
- **Moving the family's Team moves its stock.** Otherwise the variants keep reading,
  exporting and displaying as the old team while the item claims the new one. Only
  the variants on the previous family team move, so on an item split across teams the
  other team's slice is left alone.
- **Location Transfer has three outcomes.** If the destination matches another active
  stack of the same team, merge into it (the source retires only when fully drained).
  If the whole stack moves to an empty spot, relocate it in place and keep its
  number. If part of it moves to an empty spot, split off a new variant.
- **A merge target has to be the same team's stack.** Two teams can stock one
  location, so matching on location alone handed the destination team the source
  team's units: ownership changing hands as a side effect of moving a box. A split
  carries the source stack's Team for the same reason; team-less, its own team could
  no longer order it and the shortfall read as a phantom.
- **Unit rows follow the stock.** A merged-and-retired source used to keep its
  serials, and picking that real serial from the destination was refused as "already
  on record at a different location". A whole-stack relocate with no merge keeps its
  variant Id, so its rows are already right.
- **Unknown action types throw.** The controller's gates compare case-insensitively
  and the branches here don't, so a direct post of "scrap" fell through every branch,
  changed nothing, and still logged and reported success.
- **Hard holds.** A Scrap, Adjustment or partial move can't take a stack below what's
  held on it, by anyone, the reserver included. They edit or release the hold first.
- **Two saves on a move.** `ItemVariantId` on a unit row has no navigation, so EF
  can't fix up a split's new variant Id by itself and would store 0. Both saves run
  in one transaction, the caller's if there is one.

## Services/LocationCodec.cs

- **The rule.** Take the 1st, 3rd, 5th and last letter or digit of the cleaned name.
  Skip a position that doesn't exist and never use the same position twice, so when
  the 5th character is also the last it counts once. "RD Lab" is RLB, "External Yard"
  ETRD, "New Test Cells" NWES, "Plant Test Cells" PATS, "Trailer Area" TALA, "Ninja"
  NNA.
- **The only place codes are made.** The controller calls `Encode` before anything is
  written, so the database and the map always hold codes while the UI works in
  friendly names. Codes are derived and never stored, so nothing can disagree with
  the rule.
- **Decode is for display.** The reverse map is built by encoding each known name
  once, so it's the exact inverse for known locations. Anything it doesn't know
  (an unknown code, a value that's already friendly, "0", blank) comes back
  unchanged, so it's always safe to call.
- **SeedNames is a fallback.** That array used to be the whole vocabulary and it had
  drifted (see `Models/Location.cs`). The vocabulary now lives in the Locations
  table and is pushed in through `Refresh` at startup and after any Settings edit.
  The array only matters if `Decode` runs before the first `Refresh`.
- **The map is swapped whole**, never edited in place, so a reader in the middle of
  a `Decode` always sees one complete map.
- **FriendlyPath** used to be a private helper inside the Search Center view. The
  Item Card needed the same thing, so there's one copy here. The "R" prefix only
  goes on a bare rack number: many racks are stored already named ("RACK 5",
  "Lean-To", "FLOOR"), and prefixing those gave "RRACK 5".

## Services/NotificationService.cs

- **One row per recipient.** Notifications fan out on write, so dismissing is per
  person and needs no join table. Only events are stored. The live low-stock summary
  managers see is computed from inventory when the page renders.
- **Three ways to pick recipients.** `CreateForLevel` filters on access level alone.
  `CreateForSubscribers` goes to people who ticked the category in Settings, whatever
  their level, and is the one to use for anything that should be a personal opt-in
  and not a side effect of someone's tier. `CreateForLine` exists because delivery
  routing needed "one level, on these Lines", which neither of the others could say.
  Pass one Line, or a whole Branch's Lines from `OrgStructure.BranchLines`.
- **`exclude` on CreateForLine is a set, not one name.** A delivery has two people to
  leave out: whoever logged it, and the named recipient, who gets their own
  differently worded notification. Without both, an Engineer on the Line being
  notified got two notifications for one delivery.
- **CreateForLine returns the count written** so the toast can say something true.
- **`ToLower()` in the query.** EF Core can't translate the
  `string.Equals(.., StringComparison)` overload against a live query. It fails when
  the query runs, not when it compiles.

## Services/OrderService.cs

- **A cart line is keyed on item, location and team.** The same item ordered from two
  locations, or against two teams, survives as separate lines. Removing by ItemId
  alone used to wipe all of them, so Remove matches all three, which the cart posts
  back.
- **Submit checks the whole cart, not line by line.** The stock check reads saved
  Pending lines from the database, and the lines Submit adds aren't saved until the
  end. So two rows of the same item each passed against the full shelf: 4 + 4
  accepted against 5 units. Each cart line now goes into the same `StockClaims` check
  as every saved claim, together with the cart lines before it. That also closed two
  bugs: an unscoped order was invisible to a team-scoped one (BC-01), and TC was
  never counted across orders (BC-07).
- **Team resolution is shared with the Search Center card.** The rule lives in
  `InventoryService.ResolveOrderingTeam`. Only the handling of an unanswered
  ambiguity differs: an order can't proceed without the answer, a display can.
- **Notifications are best-effort.** They're sent after the commit and a failure is
  swallowed, because a notification hiccup must never fail a real order or pickup.
  Pickup alerts are an individual opt-in, so promoting someone off Standard doesn't
  cut them off from alerts they turned on.
- **A cancelled order can't be picked up.** Without the guard, a runner holding a
  stale queue page could pull real stock against an order an Engineer had already
  cancelled, and the pickup would overwrite Cancelled with Completed. An
  engineer-cancelled order's lines deliberately stay Pending, because line-level
  Cancelled means "came up short at pickup". So the order-level guard is the only
  thing between a cancelled order and a pull.
- **The order's status follows its lines.** With nothing left Pending the order is
  done. If every line came up short it's Cancelled, since nothing under that order id
  left the shelf. A mix counts as Completed: real fulfilment happened, and a short
  line's real fulfilment lives on whatever order the correction reissues. Split
  counts as fulfilment too.
- **PickUpPartialAndSplit is the "less than asked, on purpose" case.** The picker
  saw, before attempting a normal pickup, that the location they're standing at can't
  cover the line, and chose to take what's there instead of letting the pull spill to
  another location. It pulls exactly that many from that one stack and spins the rest
  off as a new Pending order so it isn't lost. Taking the full amount from wherever
  it has to come from is just a normal pickup. Compressors only for now.
- **The team boundary holds without the page's help.** Pickup Queue only offers a
  line its own team's locations, so the server check closes a direct-POST hole, not a
  UI one.
- **GetShortPullLine.** The correction form only ever came from TempData, so a
  correction that was refused (units not ticked, say) used to strand the order: the
  line stayed flagged short and nothing could bring the form back.
- **A short pull is a data-integrity event, not a way of fulfilling.** When the shelf
  can't cover a line as ordered, the line is refused and stock stays untouched. The
  picker reports the real count location by location through `ReportShortPull`.
  Nothing partial is ever pulled by `FulfillOrderItem`.
- **Holds and pickup (rule 1).** Stock held for anyone but the person who placed the
  order stays on the shelf, whoever is doing the pickup. Held stock isn't a miscount,
  so running short because of it is a refusal that names the holders, never the
  short-pull path.
- **PlanTake.** With nothing held it's the old per-stack math: at least the TC that
  is forced out when the stack lacks enough non-TC units, at most the TC that's
  there, and between those, what the line still owes.
- **Pull order.** The engineer's requested location on the line wins, then the
  picker's choice posted from the queue, then no preference. After the preferred
  stack come the stacks the requester holds, then the rest lowest number first as
  spill. The checks before the loop guarantee enough free stock, so the line is
  always fully satisfied, though it can span two locations.
- **The loan is counted from what left the shelf.** `pulledTc` is summed inside the
  pull loop and not derived as "requested minus still owed", because the floor can
  force more TC off a stack than the line asked for. The derived number dropped the
  extra: a TC motor left with no loan and its unit row still On Hand.
- **Loanable quantity comes from the shared helper.** The pickup used to carry its
  own copy of the rule, which meant `InventoryService.LoanableQuantity` had no
  callers, and adding compressors to the helper changed nothing.
- **Unit rows are matched on the shelf the unit came from.** Each pulled unit keeps
  the variant it left, so compressor rows are matched against on-hand stock at that
  location and not pooled across every stack the item sits on, and TC motor rows
  come off the shelves the TC really left. On-hand rows are fetched once per variant
  and edited in memory, because nothing is saved until the end and a fresh query
  mid-loop wouldn't see this call's own changes and could match the same physical
  unit twice.
- **Lab and serial numbers are optional at pickup.** Whatever pairs were posted are
  used in order. Missing ones are summarised on the pickup log line, never blocked.
- **The pickup log snapshots the pull locations**, because variant numbers can be
  reused later and the log can't rely on a lookup. Its quantity is what left, not
  what was ordered. Ordered-not-pulled is the bug that once let exports overstate.
- **AssignOneCompressorUnit is shared** by the normal pull, the deliberate partial
  pull and transfer approval, so all three get the same location-scoped matching and
  the same guard against grabbing a serial that lives somewhere else.
- **No serial given.** The oldest unnamed on-hand unit on that shelf is taken, so it
  isn't orphaned. If there is no unnamed row and every unit still on the shelf
  already has a recorded serial, the one leaving can only be one of those. Minting a
  fresh unnamed Picked Up row would leave a named row On Hand for a unit that's gone,
  a phantom the roster could never shed. So when the picker chose "No serial" they
  have to say which; when no per-unit choice was made at all (a correction's own
  re-pickup) the oldest recorded unit goes. When the shelf holds more units than
  rows, an unrecorded unit exists and a new row is right.
- **A serial on record at another location is refused.** Matching is scoped by
  location, so the serial isn't found on this shelf, and taking it from wherever it
  really lives would repeat the CCR-0213 mistake. The check is case-sensitive on
  purpose, to match what the unique index enforces.
- **TakeMotorUnits works per shelf.** It used to take the oldest rows item-wide, so
  pulling from shelf B marked shelf A's lab-numbered unit Picked Up and left B's On
  Hand.
- **CreateSplitOrder has to carry every line field.** Both the short-pull reissue and
  the deliberate split go through it so the lineage is recorded one way. A reissue
  that loses the team widens the remainder's pull to every team's stock; one that
  lost the TC count and requested location drew lowest-variant-first with no TC, so
  no TC stock came down, no MotorUnit rows flipped and no loan was created.
- **ReportShortPull is one action on purpose.** The picker reports what's really on
  the shelf, the stock is corrected with an audit log, and a fresh order for the
  corrected quantity is issued and picked up immediately, so the picker leaves with a
  clean pickup. It used to take a runner to find the shortage, an Engineer to cancel
  and the requester to re-order, which is why this is allowed at Standard.
  Locations left out of the correction keep their recorded quantity.
- **The reissue is scoped to the line's own team** and excludes stock other people
  hold. Reissuing against the item-wide total would promise a quantity the re-pickup
  then has to find outside that team's stock, or short-pull a second time.
- **After a rollback the change tracker is cleared.** The rolled-back changes are
  still tracked in memory, and what the controller reads next, to put the form back,
  has to be what the database holds.
- **LoanUnitsToSettle.** Units are a partial overlay: a line of 3 may name 1 unit and
  carry 2 as plain quantity. The ticks and the quantity still have to describe the
  same units. Every ticked unit must be out on this loan line (the lookup used to
  match on ItemId alone, so an id from another person's loan of the same model was
  flipped while their counter stayed). No more may be ticked than are being settled
  (every tick starts checked, so lowering the quantity alone put 2 rows On Hand
  against 1 unit returned). And the unticked ones have to fit in what the line
  carries as plain quantity, or a recorded unit would stay Picked Up on a loan that
  says it's settled.
- **Returns go back to the team they were pulled for**: the order line's team, or the
  item's family Team for a line from before the split. Without it a return to a new
  shelf mints a team-less stack that still counts toward the item total, so its own
  team sees a phantom shortfall. Returning into another team's stack is refused
  because it would hand them the units.
- **Motor loans settle oldest first.** TC motor units have no serial for a person to
  tick, so on a Return or Scrap the oldest units still out on that order line are the
  ones that change. It's scoped to the order line so a return can't pull in another
  order's outstanding units for the same model.
- **Returned units land on the shelf they were returned to.** Scrapped units never
  come back: the status is final and the row stays, so the serial's history can
  still be read after the machine is gone. A loan scrap adds no stock, because the
  stock already left at pickup.
- **Only a Pending order can be cancelled.** It mirrors pickup's refusal of anything
  not Pending. Without it a stale Order History tab could flip an order that was
  already picked up to Cancelled while its lines stayed Completed and the loan stayed
  out.
- **Transfers are parallel to orders, not built on them.** A transfer never creates
  an Order or OrderItem row, so an item can be ordered by its own Line and
  transferred to another at the same time. They only meet at the stack quantities
  both decrement. It's one-way with one approval: it was first built as a Borrow
  with a due date and a Return flow, but a transferred unit never comes back in
  practice. Approval needs Engineer and up on the item's Line and team, enforced in
  the service and not just hidden in the UI.
- **A transfer request is capped at what's available to give.** It used to be capped
  against nothing, so a request for 500 of a 3-unit slice sat in the owning Line's
  queue looking legitimate until approval refused it. The cap nets out Pending
  orders and holds: units another team already has on order aren't spare.
- **TransferPullOrder has two callers on purpose.** `ApproveTransfer` executes it and
  `PlanTransferPull` hands the same answer to the approval page, so each unit's
  serial picklist offers the shelf that unit is really coming off. When the page
  pooled every location's serials into every slot, a reasonable pick got refused by
  the location-scoped match.
- **Approval is measured against availability, not shelf quantity.** Raw shelf
  quantity counts units a Pending order has already been promised, so approving
  against it could turn someone else's clean pickup into a short pull. A transfer
  and an order compete for the same units on the same terms.
- **Approval is measured for the requester, not the approver.** An approving
  Engineer's own holds must not become transferable just because they're the one
  clicking.

## Services/OrgStructure.cs

- **Managed, not hard-coded.** Settings can add and soft-hide Branches and Lines
  through the Branches and OrgLines tables. The class stayed static with no database
  access, the same shape as `LocationCodec`, so nothing that reads `BranchLines`,
  `AllLines`, `BranchFor`, `GroupFor` or `IsValidLine` had to change.
- **Line is a plain string** on users, items and teams, not a foreign key, so
  hand-written SQL stays simple (`UPDATE InventoryItems SET Line = 'X'`). Visibility
  is plain string equality, so hiding a Branch or Line can never orphan data that
  already carries it.
- **GroupFor.** Group stopped being a form field. It's derived from the Line of the
  person registering the item, and its first letter becomes the ItemId prefix
  (Commercial C, Residential R, Sustaining S). The id records who created the item,
  which a later Line reassignment can't change after the fact. A blank or unknown
  Line falls back to "Commercial", which is what the first 487 items carry, so
  nothing renumbers.

## Services/RequireLevelAttribute.cs

- **What a blocked user sees.** A banner through TempData and a redirect to the
  dashboard, or a 403 with the message for an AJAX call.

## Services/RequireNameFilter.cs

- **Why it re-reads the roster on every request.** Sign-in used to be the only place
  level, Line and Branch were read, and the idle timeout slides, so an active session
  kept its old rights indefinitely. Now a demotion, a Line change or a hide in
  Settings applies on that person's next click. Global filters run before every
  `[RequireLevel]`, so that check sees the fresh level.
- **Only MVC actions.** Static files don't go through it. Actions marked
  `[AllowWithoutName]` are skipped so the redirect to Identify can't loop.
- **The lookup lowercases both sides** because the UserName column compares binary,
  and a name typed in another case still has to find its row. No row means an
  off-roster name, which is Viewer with a blank Line and Branch, exactly what sign-in
  gives it.
- **Session writes only when something changed.** A write sends a fresh cookie and
  re-commits the session, which an ordinary click doesn't need.

## Services/RequireSuperuserAttribute.cs

- **Two independent checks.** The session name has to be the configured superuser
  name, and the passcode has to have been entered in this session. Neither is derived
  from AccessLevel, so Settings can't be opened by raising someone's tier in the Users
  table.

## Services/ReservationService.cs

- **Instance and static halves.** The instance methods are what the Reserve dialog
  and Reserved Items call. The static ones are called by pickup, stock changes and
  the expiry sweep from inside their own transactions with their own context, so a
  hold changes in the same save as the stock movement that caused it. The spec is
  `docs/Unit_Reservations_Spec.md`; the rule numbers below are its rules.
- **Pickup uses the orderer's hold (rule 1).** Whoever physically picks the order up,
  it's the requester's own holds that go down by what the order took off each stack.
  A reservation that reaches 0 ends as Picked Up.
- **A recount beats a hold.** A short-pull correction is a physical recount, so it's
  never refused because of a hold. Holds on that stack shrink to what's really there,
  newest first, and their reservers get a notification.
- **A whole-stack merge** takes the holds with the stock to the stack it merged into.
- **Named units follow the counts.** `TrimPins` drops the name of any unit that left
  the shelf, and when a stack names more units than its line holds, the
  newest-recorded names go first.
- **Who may do what (rule 3).** Reserving is Standard and up on your own Line, Admin
  anywhere. Changing or releasing a hold is the reserver, Engineer and up on the
  item's Line, or Admin.
- **Dated holds end on their own (rule 7).** The reserver hears about it twice: once
  on the last day ("ends today") and once when it ends.

## Services/ReservationSweepService.cs

- **A missed run costs nothing but time.** Every query already treats a hold past its
  date as not holding, so the sweep only writes the log row and sends the
  notification. The host is always on, so it runs once after startup and every 10
  minutes.
- **It must never take the app down.** A failure (no database yet, a locked file) is
  written to the console and retried on the next round.

## Services/StockClaims.cs

- **One place for the answer.** Item Card, Search Center, Submit, Pickup Queue,
  transfers and Reserve all ask this class how much a person can still take, so none
  of them can disagree with another. The spec is section 4 of
  `docs/Unit_Reservations_Spec.md`.
- **Supply and claims.** Supply is each active stack's quantity and TC count, grouped
  by the stack's Team (a blank Team is its own group). Claims are live holds, pinned
  to their stack, and Pending lines on Pending orders, scoped to their Team or
  unscoped when the Team is blank.
- **Coverage.** A reserver's own pending lines draw on their own holds first, so a
  held unit its reserver has ordered isn't counted twice.
- **When the claims fit.** Every stack's holds fit the stack; every team group's
  holds plus scoped lines fit the group; and the unscoped lines fit what the groups
  have left over. The same three checks run again for TC. This is Hall's condition
  for this shape of problem. `Deficit()` adds up how far each check is over.
- **Available is measured against today's deficit.** It's the most that can be added
  without raising `Deficit()`. So data that is already over-claimed (an old
  overbooking, drift) never blocks an unrelated request.
- **Two bugs this closed.** The old team-scoped count ignored unscoped lines
  entirely; here they compete with every group through the left-over term (BC-01). TC
  was never counted across orders; here it's a second dimension of the same check
  (BC-07).
- **A hold on a stack that is no longer active** can't be honoured at all, so its
  whole quantity counts as deficit.

## Services/SuperuserGateService.cs

- **A second lock, on purpose.** Settings is what edits AccessLevel, so it can't be
  guarded by AccessLevel alone. The allowed name and the passcode live in
  configuration and never in the database, so neither can be changed or discovered
  through the thing it guards. It isn't meant to be rotated from inside the app.

## Services/TrackedUnitService.cs

- **Rules are enforced here, not in the page.** Every access rule is in this service
  or in `[RequireLevel]` on the action. Hidden buttons are cosmetic. The one rule that
  can't be an attribute is Update's: Standard may edit a unit, only Engineer may move
  it to another Line or Team.
- **The server is the only validation that counts.** Forms elsewhere in VIS submit
  through `form.submit()`, which skips HTML validation. So every text field is
  trimmed, every over-length value is refused with a message naming the field (never
  silently truncated), and Package / Split, Line and Team are validated here.
- **Add uses a transaction.** The log's details name the unit ("Unit #12") and the Id
  only exists after the INSERT, so the log can't ride in the same `SaveChanges` the
  way it does for Update, Notes and Delete. One transaction instead.
- **Update: posted or not posted.** Line and Team arrive null when the form didn't
  post them (a Standard user's disabled selects), which means leave alone. A posted
  value that differs from the stored one is a move and needs Engineer. It's validated
  only when it changed, so a Line or Team hidden in Settings since the row was saved
  can still ride through an untouched Save. Package / Split follows the same rule.
- **Notes text is never logged.** It can be long, and everyone is free to write it.
- **SetWhereabouts and SetBuildStatus** each touch their one column and nothing else,
  so they can't move a unit's Line or Team. Their messages carry no typed text,
  because the page shows them in a toast built with `innerHTML`.
- **Clean folds line breaks.** A textarea's maxlength counts a break as one character
  but the browser posts it as two. Counting two would refuse a Notes box the form
  just accepted.
- **Blank ItemId on the log rows** is VIS's marker for "not about an inventory item".
  A tag doesn't exist yet, and a non-blank value that isn't an item would be read as
  a deleted item.
- **TrySave** catches the case where the row vanished between the load and the write
  (EF sees the UPDATE or DELETE hit zero rows). Nothing was changed, so the caller
  reports "no longer exists".
- **Duplicate warnings warn and never block.** Whether to block is still an open
  question for Conner. With no serial to go on, the same model and Sample # on
  another row that also has no serial is probably the same physical unit entered
  twice.

## Views/Home/_AlertRulesPartial.cshtml

- **Apply to all closes one modal before opening the next.** The alerts modal is
  closed first and the confirm popup opens only once it's fully hidden. The app pins
  every modal to the same z-index, so two stacked modals would misbehave.

## Views/Home/_ItemCardPartial.cshtml

- **The Item Card's buttons don't act directly.** Each one carries a
  `data-card-action`. The page's script routes it to the existing Add to Cart, Modify
  Stock, Request Transfer or Delete Item modal after the card has finished hiding, so
  two Bootstrap modals never stack. That's the same chaining every modal-to-modal hop
  in the app uses.
- **"Awaiting Done Using".** For a compressor the loan counter means picked up and
  not yet dispositioned, since compressors are consumed and not returned. The card
  labels it for what it is.

## Views/Home/_ModifyStockPartial.cshtml

- **`msMyTeams` is declared in the partial.** The partial renders on both Command
  Center and Search Center, and a top-level `const` in one `<script>` tag isn't
  visible to an earlier one. That's the break `15822b4` had to fix. It isn't named
  `myTeams` because `SearchCenter.cshtml` already declares that at page level, and a
  duplicate `const` in shared global scope is a SyntaxError that kills the whole block.
- **Location / Variant picker.** It picks which physical stack the action hits. On a
  single-location item it's hidden for everything except Add, and hidden it posts
  nothing, which means the primary variant. Add always shows it, for the "+ New
  location..." option. A new location on Add reuses the Location Transfer cascade for
  the address and hides the move-quantity row, since the main Quantity box is the
  amount.
- **Team filter on the stack list.** It narrows the stacks to one team's slice. It has
  no `name`, so it's a client-side filter that never posts. `variantTeam` is a
  different thing: the team a new stack is created for. The filter is disabled unless
  the item is really split across teams, because offering "All teams / Samurai" on an
  item only Samurai has ever touched is a choice that isn't one.
- **The filter's default only fires when the item changes.** A new item opens on the
  viewer's own slice when exactly one of the item's teams is theirs. Two of theirs, or
  none, opens on the full list instead of guessing. Keying the default on the filter
  being blank made "All teams" unreachable: every rebuild read the blank as "nothing
  chosen yet" and re-applied the default, so the other team's stacks could never be
  listed again. Narrowing rebuilds the stack list, which changes what's selected, so it
  runs the same follow-ups a manual pick does.
- **Team membership is a list.** Someone on Samurai and Ninja owns both slices of a
  split item and isn't warned about either. A blank team fails open, the same as Line
  everywhere else. `msItemTeams` is the page's twin of
  `InventoryService.ActiveVariantTeams`: one team or none means the item isn't split
  and team gates nothing.
- **Another team's stack: warn, don't refuse.** Acting on another team's slice is
  sometimes exactly right. A runner correcting a miscount doesn't belong to the team
  that owns the shelf. So the note says whose stock it is and doesn't gate anything,
  and the server has no matching refusal.
- **Team for a new stack.** It only means something on Add's "New location..." branch.
  An existing stack keeps the team it already has, the same way it keeps its location.
  Blank falls back to the item's own Team. Setting it is how a second team gets its own
  claim on an item.
- **Deleting an empty stack (Admin).** A zero-quantity stack on a multi-variant item
  has no other way to go away. Delete Item needs the item's total at 0, and this one
  stack drained to 0 while its siblings still hold stock. The button shows only when
  the selector is showing a real stack (not "New location...") on an item with more
  than one, and that stack is empty.
- **Merge note on a transfer.** When the destination matches a sibling stack,
  executing merges the quantities, and the source retires if it's drained. The check
  mirrors `ModifyStock`'s merge-target lookup exactly, team included, because the
  server only merges into a same-team stack. Promising a merge here that the server
  then declines is the same page-and-server-disagreeing shape every Pass 32 bug turned
  out to be. When the spot belongs to another team, the note says the units move there
  as their own stack.
- **Location Transfer cascade.** It's item-first: the fields seed from the selected
  stack's stored location, which is the transfer source and not the item-level primary
  (the same thing on a single-location item), and then they can be edited. Major and
  Sub options come from the real coded hierarchy, `locHierarchyData` (Parent → Major →
  Subs). 0 means unspecified and can be picked at every level below Parent. Setting a
  level to 0 leaves the levels under it with only their 0 option, because the
  hierarchy has no children under a 0 segment. A stored Parent code that isn't a known
  option gets added, so a real value is never silently dropped. The Rack and Row
  suggestions don't need `encodeLoc()`, because these selects already hold codes,
  unlike the name-valued `reg-` and `exp-` ones.
- **Recorded units on a partial transfer.** Mirrors `ModifyStock` /
  `TickedUnitsMoving`. Moving the whole stack takes every recorded unit, and moving all
  of a motor stack's TC takes every TC row, so there's nothing to ask. Anything short
  of that can't tell which units went, so the person ticks them. The server applies the
  same minimum and maximum and refuses a pick that doesn't fit; the page only asks and
  keeps count. The list is rebuilt when the source stack, the quantity or the TC count
  changes, keeping ticks that still apply, and it's emptied (so nothing posts) when
  there's nothing to ask.
- **Recorded units leaving on a Scrap or downward Adjustment.** Mirrors
  `InventoryService.RetireSurplusUnits`. Once the action lowers the stack's count (its
  TC count, for motors, whose rows are TC-only), the On Hand units on record can't
  outnumber what's left. Taking the whole stack takes every row: the page says so and
  there's nothing to tick. A count that still has room for every row asks nothing.
  Otherwise exactly the surplus has to be ticked. `leavingTargets` is the server's
  arithmetic line for line, Scrap's forced-TC floor and Adjustment's TC reclassify and
  clamp included, because the question has to match what the server will compute.
- **Execute waits on the leaving checklist.** It stays disabled until the checklist has
  exactly the ticks it asks for, so a count typed with the units forgotten can't be
  submitted. The server enforces the same count and refuses a pick that doesn't match.
- **One checklist for both pickers.** `renderUnitGroup` builds both. `g.min` and
  `g.max` bound how many can be ticked. Once the maximum is ticked the rest grey out,
  and the help line turns amber while fewer than the minimum are.
- **Thermocoupled count (motors only).** Two inputs share the name `thermocoupledQty`:
  one for Add, Scrap and Adjustment, and a twin in the transfer move-quantity row.
  Whichever is hidden is disabled, so exactly one ever posts. Each is capped so nobody
  can enter an impossible number. On Scrap and Transfer the floor is the TC that has to
  go when there isn't enough non-TC stock to cover the amount.
- **TC on an Adjustment is a reclassify.** It marks existing on-hand stock as TC ("take
  my current N, mark X of those as TC"), independent of whatever quantity change the
  same adjustment makes. Adding new TC stock is Add's job. The box is capped at what
  isn't TC at that location yet.
- **Type checks stay in lockstep with the server.** `isMotorType` matches
  `InventoryService.IsMotorType`: a type ending in "Motor" (Motor, ID Motor, OD Motor)
  is TC-eligible and nothing else is. `isCompressorType` matches `IsCompressorType`,
  exact match only.
- **Compressor serials on Add.** Add here gets the same serial capture New Item
  Registry has (`reg-serial-rows`): optional, one box per unit, using the `serial_N`
  names `CreateItem` already reads. It shows only on Add for a compressor, and it's
  rebuilt when the quantity or the action changes.
- **Quantity bounds.** Scrap is capped at the selected stack's quantity. It acts on one
  variant, so the item total would over-promise on a split item. Add and Scrap have a
  floor of 1. Adjustment stays signed but can't take the stack below 0, so its floor is
  minus the stack's quantity. These are clamped live instead of left to min/max validation,
  because Execute goes through `executeSubmit()` → `form.submit()`, which skips
  validation. The server clamps and rejects on its own either way.
- **Internal - Transfer moves Branch and Line, not Group.** It used to offer a "New
  Group". Those options mixed two branches (Commercial, Residential) with one line
  (International, which sits under Commercial Air) as if they were peers, a flattened
  and wrong copy of `OrgStructure`. Group is also derived from the registering user's
  Line and frozen at creation, so letting a transfer change it would put an item's
  Group at odds with its own ItemId prefix. Ownership now moves what actually governs
  visibility. It takes effect immediately, with no transfer window, and a blank Line
  means visible to everyone.
- **The Ownership selects keep their `edit-` ids.** Branch and Line sat in Edit Details
  for a while, back when it was the only Engineer-and-up pane, which left Internal -
  Transfer with nothing to do. They moved here with the same element ids, so
  `populateEditLinePickers()` drives them unchanged. The pane seeds from the item's
  current Line through `getStockItem()`, the same lookup Edit Details uses, because
  `stock-item-id` is a read-only text box and not a select with options to read. The
  three inputs stay disabled outside this action, so a hidden pane can't add a stray
  value to an unrelated submit.
- **Team on Internal - Transfer.** The list comes from the Teams table and blank is
  allowed, since team is optional. The project code follows whichever team is chosen
  and is resolved on the server. Picking a Team suggests its home Line, the same
  convenience-not-lock as registration. It only reacts to a person changing Team after
  the pane has seeded.
- **Edit Details is identity only.** Rheem PN, Description and Brand, the family-level
  fields. The inputs stay disabled unless this pane is active, so they never post
  alongside a stock action, and they're prefilled fresh from the selected item each
  time so the person edits what's really there. The Rheem PN is unique across items:
  the "Taken" warning checks the loaded item list as you type, and the server rejects a
  taken PN on its own. "N/A" is exempt, since it's the shared placeholder. N/A and blank
  are the same state: both land on the N/A toggle with the box read-only, not disabled,
  so it still posts "N/A". Brand is fill-if-blank only. A real brand already on the
  item locks the field.

## Views/Home/_NewItemRegistryPartial.cshtml

- **The name box suggests but never blocks.** Registering something new is the point
  of the form, so free text always submits, unlike Modify Stock's search. Clicking a
  match means "this already exists": it jumps into Modify Stock for that item and
  drops the registration.
- **Group isn't on the form.** The server derives it from the Line, and it only
  exists to mint the id prefix. Pinning it at creation means a later Line change
  can't make an existing id wrong. Team is optional with N/A as the default: a team
  is a reporting label, and visibility belongs to Line.
- **Rheem part number is required** on every new item, read off the physical label.
  The server rejects blanks and duplicates; the page only gives the live "already
  exists" warning. "Has a Part #" is the default and N/A is the deliberate opt-out.
- **The Parent option's value is the name, not the code.** `bindCascadingLocation`
  encodes whatever is selected. That difference is why the sweep that moved the
  location lists onto the Locations table missed this one and the export filter:
  they were searchable as `value="RD Lab"`, not `value="RLB"`.
- **Team suggests a Line.** Picking a Team fills in its home Line but doesn't lock
  it. A manual change afterwards stands until a different Team is picked, which
  suggests again, on the read that re-picking a team on purpose is more common than
  wanting to keep an unrelated override.
- **The Item ID box is a preview.** It predicts the id from the Branch, Line and Type
  using the items already on the page, and falls back to the Branch dropdown so it
  updates as soon as a Branch is chosen. The controller recomputes and assigns the
  real id at save, so a stale preview can't cause a clash. The success toast shows
  the real one.
- **Unit capture at registration is optional.** Serial rows appear for a compressor
  and a thermocoupled count for a motor, based on the Type typed. Both reset each
  time the modal opens so a previous registration's rows don't linger.

## Views/Home/CommandCenter.cshtml

- **The map and the Location List read one object.** The view builds a map of Parent
  name to row counts, alert flags and Majors, and serializes it once. The pins and
  the list rows both read their counts from it in the browser, so they can't
  disagree.
- **Styles come from the `--vis-*` tokens.** Command Center picked up the sidebar's
  light, shadow-card look ahead of Search Center. Building it from the existing
  tokens means it adapts to both themes with no override rules. The map pins and
  popovers were ported from the old dashboard, which hard-coded dark values and so
  never adapted to the theme toggle.
- **The gear on Stock Alerts** sits above the card's stretched link, so it opens
  Alert Rules instead of following the card to Search Center. After the old
  dashboard page was retired, nothing opened the Alert Rules modal at all: the
  partial was never wired into either new page, and per-item and bulk thresholds
  were unreachable until the gear was added.
- **Activity Feed labels fall back to the action's own name.** "Stock Adjusted" used
  to be the default, which mislabelled Edit Details, Unit Logged, Stack Deleted and
  Loan Scrap as stock changes. A new action type now reads correctly with nothing to
  update.
- **Page constants come before the partials.** `itemsList`, `orgStructure` and the
  rest have to render before New Item Registry and Modify Stock, because both
  partials reference them at the top level of their own scripts.

## Views/Home/Deliveries.cshtml

- **The photo opens full size.** The card thumbnail is a 160px centre crop, so a
  tracking number near an edge isn't on screen, while the stored file is a full
  1600px JPEG. The thumbnail is a button and not an image with a click handler, so
  it's reachable by keyboard and announced as something you can activate.
- **One modal for the whole board.** The image source is swapped in on click, so a
  board of 30 deliveries still renders one modal.
- **Only the photo's background is forced dark.** A neutral surround is what makes a
  letterboxed photo readable. The modal chrome follows the theme: forcing the whole
  modal dark put the light theme's title colour on a dark panel at about 1.1:1.
- **"Open full size in new tab"** hands the JPEG to a real browser tab, which is what
  lets someone pinch and zoom into a tracking number. No in-page widget beats the
  browser's own zoom.
- **Values are read from data attributes.** A recipient label is a real person's
  name, and an apostrophe interpolated into an inline click handler would break it.
- **The image source isn't cleared on close.** Clearing it when the modal hid raced
  the next open: close-then-reopen let the clear land after the new source was set,
  and the modal came up blank. Setting the source before showing already guarantees
  the right photo.

## Views/Home/Identify.cshtml

- **The name picker matches on display name** because people know their own name,
  not necessarily the First.Last login form. Picking one sets the input to the login
  name, which is what the server parses.

## Views/Home/Intake.cshtml

- **"Already registered" is a live section, not a list behind a button.** A row whose
  typed name exactly matches an existing item is pulled out of the grid and becomes
  its own inline stock-update section, carrying over the quantity, serials and TC
  count already typed. A second row for the same item grows that section instead of
  opening another, so whatever was typed into it survives. Those rows are excluded
  from Import and go through Apply All, a separate form and endpoint, because one
  form can't nest inside another.
- **The Type gate in the page.** A Type is accepted only as an existing type, picked
  or typed in full (case doesn't matter; the stored spelling wins), or as a new type
  someone explicitly chose "Add as a new type" for. A free-typed miss like "Contro"
  used to register as its own type and mint its own id prefix (CCO- instead of
  CCL-). On leaving the box a case-only miss snaps to the stored spelling. The server
  re-checks all of it, and the `typeNew` flag is what tells it a new type was
  deliberate. Every named row needs a valid Type before anything is sent, Preview
  included.
- **One `serial` input per row, always.** The server binds `serial[]` by position
  against `itemName[]`, so a row that skipped rendering one would shift every row
  after it. Non-compressor rows get it hidden and empty instead of removed. The same
  goes for `typeNew` against `type[]`. Extra serial slots and the TC box have no name
  until submit, because rows can be added and removed right up to then and the index
  has to come from the final order on the page.
- **The location selects hold codes here.** Registry and Export use name-valued
  selects and encode them; Intake's are already codes, so the Rack and Row
  suggestions need no encoding step.
- **Branch and Line autofill.** The selects are seeded from the signed-in user's own
  assignment on load: a specific Line wins, a whole-Branch user gets the Branch with
  the Line left to pick. Picking a Team with a home Line overwrites both, because
  that's a deliberate act, and they stay editable. None of it touches the server's
  validation.
- **The table is wrapped for sideways scrolling.** The row of inputs needs more than
  a phone's width however the columns are proportioned, and without the wrapper the
  table forced the whole page wider.
- **Shared helpers.** The page used to carry its own copy of
  `bindValueAutocomplete`, which silently shadowed the shared one in `site.js`. It
  still defines its own `encodeLoc` and `racksRowsFor`, which `site.js` also has.

## Views/Home/LogDelivery.cshtml

- **The recipient list is grouped by tier**, engineers first, instead of one flat
  list of 44 names. Engineers are the most common pick and the people who work a
  delivery. Each name shows its Line so the person logging can see who else the
  notification will reach.
- **The "Have it / N/A" toggles** work like the Rheem part number toggle on New Item
  Registry. N/A makes the box read-only with the literal value "N/A", so something
  real still posts.

## Views/Home/MyActivity.cshtml

- **Loans come first on the page** because that's where the action is: things you owe
  back.
- **Compressor lines are gated behind Done Using.** Return and Scrap stay hidden
  until the person says they're finished with the unit. Nobody one-clicks a scrap by
  accident, and a line nobody has finished with stays quiet. It's UI only: no state
  is stored, so a refresh puts the gate back and nothing is half-committed. The
  counter on those lines reads "In use", because a compressor is normally consumed.
- **One tick list for two forms.** The unit checkboxes sit outside both the Return
  and the Scrap form, and `syncUnits` copies the ticked ids into whichever form is
  submitted. Inside one form, the other action would always act on every unit or on
  none.
- **The outstanding count has its own light-theme colour.** The yellow brand accent
  is deliberately the same in both themes, and it's about 2.15:1 on the white card.
- **The approval queue lives on this page**, not a separate one. Each unit's serial
  dropdown lists only what's on hand at the shelf that unit will come off, in the
  same order the approval pulls. A serial another slot has claimed is greyed out the
  moment it's picked, which is friendlier than a bounced submit; "No serial" and
  "add serial" are exempt because several units may have none.

## Views/Home/OrderDetails.cshtml

- **Two badges for a short line.** `Corrected` is what a short line becomes once the
  correction acts on it. Keying the badge on `Cancelled` alone dropped it at exactly
  the moment its tooltip became true: a corrected order showed a Cancelled header
  over an unannotated line with nothing pointing at the reissue. The two states get
  their own wording because one still needs a picker to act.

## Views/Home/PickupQueue.cshtml

- **The sticky notes are cards with an amber edge**, not full-yellow cards, which
  would fail contrast in dark mode.
- **One shared form for a partial pickup.** The card is hidden until "Take these,
  short-pull the rest" is clicked on a shortfall banner. The script fills in its
  hidden fields, description and serial rows and then shows it. The short-pull
  correction card works the same way: one form populated per click, not one per line.
- **The serial picker only names units with a real serial.** A serial-less row
  rendered as an option gets the value "null", it's selectable, and the staggered
  default would even open on it, so a picker who touches nothing posts the literal
  serial "null". The server filters those rows out of the data too; the client check
  keeps the page honest if that ever changes.
- **Options are real nodes, not HTML strings.** Serials and lab numbers are typed by
  Standard-level users and land in every picker's session.
- **Staggered defaults.** On a multi-unit line each dropdown opens on a different
  real serial while they last, then on "No serial", so they don't all aim at the same
  physical unit. A serial one dropdown has claimed is disabled in its siblings and
  freed again when that dropdown moves off it. The server rejects a duplicate anyway;
  catching it before Confirm is friendlier.
- **Which units are gone.** On a short-pull correction, a count typed below the units
  recorded on that shelf has to say which of them are gone: exactly the surplus. A
  count of 0 takes every one with no question, and a count that's unchanged or higher
  asks nothing. Motor rows are TC-only, so their surplus is rows minus the TC that
  still fits. The submit button stays disabled until every tick count is met, so the
  common mistake (a count typed, the units forgotten) can't be submitted. The server
  enforces the same count and puts the form back if it refuses.

## Views/Home/SearchCenter.cshtml

- **Six filter boxes on one row.** The three attribute filters sit beside the three
  location levels instead of in two stacked rows. Each box is a third of its group
  and not half (the shared `.filter-grid` is two columns, so three children wrapped
  2 + 1 and every box ran long), and the card drops from three rows tall to two.
  That second effect is what closed the dead space under Omni Search: `.grid-card`
  is full height, so the Omni card stretches to whatever this card's height is. The
  override is on this page and not in `site.css` because Search Center is the only
  user of `.filter-grid`. Below about 1200px the two groups stack, and on a phone
  each group goes single-column.
- **Type and Brand are dropdowns.** They used to be free-text boxes you had to spell
  right. Both are closed sets in practice, so the filter offers what exists.
- **The location filter replaced a free-text "FDA Term".** That box asked people to
  type a machine string ("ETRD.CNEA.CNE6") and matched it as loose text, so a rack
  called "2" pulled back 305 items instead of 43. The selects post the stored
  Parent, Major and Sub codes while showing the real names, and the server matches
  the columns.
- **The filter script stands alone.** Every option is rendered by the server with
  its parent and major in data attributes, so narrowing is just show and hide. It
  doesn't use `locHierarchyData` (declared in the Modify Stock partial; declaring it
  again would be a duplicate `const` in shared scope) or `locDecode`. It waits for
  `DOMContentLoaded` so it never queries markup that hasn't parsed yet, the failure
  that silently killed the sidebar wiring in `site.js` for three commits.
- **Sub narrows on both levels.** A Sub only means something under its own Major,
  and two Majors can hold Subs with the same display name.
- **Actions are not filters.** Export, Modify Stock and View Cart are things you do,
  so they read as an Actions row like Command Center's Quick Actions. The quick
  filters are two boxes and not one strip because they're two different axes: Type
  answers "what kind of part" and Branch answers "whose stock". Only the Branch
  buttons grey out for branches you're not on.
- **"Available" on a result row is scoped to the viewer's team**, resolved the same
  way Submit resolves it. The item-wide figure promised a split item's whole stack
  to whichever team was looking, and then Submit refused at the team's real
  ceiling. It falls back to the item total when no single team applies, which is
  what Submit would allow too.
- **One entry point per row.** Add to Cart, Handle Stock, Request Transfer and
  Delete Item all live in the Item Card, which also shows everything else VIS stores
  about the item. Clicking anywhere on the row opens it, and Enter does the same
  from the keyboard. Cart rows don't, because they carry their own Remove form.
- **The registry's Open button sits outside the collapse toggler.** Bootstrap 5
  binds its delegated handlers in the capture phase, so a `stopPropagation()` on a
  button inside the toggler runs too late and the row expands anyway.
- **Script order, twice over.** A top-level `const` in a later script tag isn't
  visible to code that runs in an earlier one, even though both share the page's
  global scope, because the browser runs tags in document order. So the page
  constants render before the Modify Stock partial, whose script reads
  `orgStructure` and `itemsList` at top level. And `locMap` can't live inside a
  function: `bindCascadingLocation` in `site.js` reads it as a free variable from
  its own scope, so a function-local `locMap` is invisible to it and the first
  Parent pick in the Export Wizard threw a ReferenceError.
- **`afterModalHides`.** Every modal-to-modal hop waits for the first modal to
  finish hiding before opening the next, so two Bootstrap modals and their backdrops
  never stack. The Item Card's buttons only route to the existing modals; the card
  never duplicates their logic.
- **The Item Card and the Reserve dialog are fetched on open.** The card's cart
  state and pending-order counts are never a stale snapshot from page load, and the
  Reserve dialog's holdable counts move with every order and hold. A refusal from
  the Reserve form comes back as plain text and is shown as text.
- **Naming a unit holds it.** Ticking a unit in the Reserve dialog bumps that
  stack's count up to at least the number of units named, and for motors the TC
  count too, since a named motor unit is a TC unit.
- **Add to Cart's team picker** only shows when the item's variants span more than
  one team and the signed-in user belongs to more than one of those teams. Everyone
  else never sees it.
- **Request Transfer.** It's offered instead of Add to Cart when the item belongs
  to another Line. It's one-way, with no due date and no return flow. Serials for
  compressors are picked later, at the owning Line's approval step, the same way a
  normal order never asks the requester to pick serials. The location dropdown has
  no "Either" option, so once a variant carries a Team that pick also says which
  team's slice is being requested, which is why the team tag is shown on each
  option.
- **The Export Wizard's Group list comes from real data.** The old fixed list
  offered "International", which is a Line under Commercial Air and never a Group,
  so that option could never match and always returned an empty CSV.
- **The Export Wizard's location selects** post real codes the server matches
  against the columns, so each level is optional. They used to be folded into a
  hidden dot-joined string that the server prefix-matched; skipping any level
  produced a string that matched nothing and returned an empty CSV with no error.
- **Rack and Row suggestions in the Export Wizard** are a union across every
  location consistent with the levels chosen. The shared helper needs the full
  Parent, Major and Sub, which was fine while a partial pick couldn't be queried
  anyway. Now that Rack alone is a valid filter, an exact lookup would leave the box
  with no suggestions in exactly the states that newly work. The selects already
  hold codes, so nothing is encoded again.

## Views/Reservations/_ReserveForm.cshtml

- **The Reserve dialog has no script of its own.** It's fetched from
  `/Reservations/Form` and set as the modal's content, and a script inserted that way
  never runs. Every handler is a delegated listener in `SearchCenter.cshtml`, keyed on
  the form's id.

## Views/Reservations/Index.cshtml

- **How the page works.** Rows are ordered the way the controller builds them: yours
  grouped by reason, then your Line, the rest of your Branch, and the other branches
  greyed. A row opens its Item Card on Search Center, and Edit opens the Reserve
  dialog there. The filters run in the browser because the whole list is one page.

## Views/Settings/Index.cshtml

- **Every control is a plain server round trip.** The Team Membership picker follows
  the same pattern: picking a team reloads the page with the team in the query
  string, so the controller can render that team's ticked and unticked roster.
- **The owner picker for Add Location** is built from the same rows the table
  renders, so it can't offer an owner that doesn't exist. The lists are serialized
  as JSON and not interpolated into a string: a quote or backslash in a location name
  used to break out of the literal and kill the whole script block.
- **The zone editor works in fractions.** Everything is normalised against the
  overlay's rendered box, so the numbers saved are fractions of the image and don't
  depend on how large it's drawn on this screen. Drags are clamped to the image,
  because a drag that leaves it would post coordinates the server rejects, which
  reads as the save mysteriously failing. Dragging up or left is normal, so the box
  takes the minimum corner and an absolute size.
- **"New parent" in the zone form.** Choosing it swaps the dropdown for a name box.
  The name field only matters when it's visible: an empty name means "use the picked
  Parent", which is what the server branches on.

## Views/Shared/_Layout.cshtml

- **Scripts load at the top of the body.** jQuery, Bootstrap and `site.js` used to
  load at the very bottom, after a page's own inline script had already run, which
  is why a shared helper couldn't live in `site.js`. Everything those scripts touch
  synchronously is either above that point (the sidebar shell) or added by the page
  body further down, so only the order scripts run in changed.
- **The low-stock summary follows Line visibility.** Admin sees every Line. A user
  with a Line sees that Line's low-stock count plus unassigned items. A blank Line
  fails open. Quantity lives on the variants and the item's own Quantity can't be
  used inside a query, so the badge sums each item's active variants database-side.
- **Deliveries and the low-stock line use different checks.** The Deliveries link is
  Engineer and up; the low-stock line in the bell is Management and up. Folding them
  into one flag would quietly hand Engineers a second thing nobody asked to widen.
- **Sidebar active state.** The Home links match on the action name, so they also
  require the Home controller. Otherwise Unit Tracker and Settings would light up
  Command Center too, since all three are an action called "Index".
- **The sign-in page has no shell.** There's nothing to navigate to before signing
  in, and every other route bounces back there, so the page is one centred card.
- **The cart button.** The cart used to be one plain card in the Actions row and was
  easy to miss. It's a fixed button in the same spot on every page, red when empty
  and green with a live line count once it holds anything. Its text colour needs
  `!important` because the scoped layout stylesheet styles every link with an
  attribute selector that outranks a plain class, which made the label link-blue on
  the red fill at 1.05:1.
- **Toasts take text, not markup.** `showToast` sets the message as a text node,
  because toast text carries user-typed names and server messages. Only the icon is
  markup, and it's one of two fixed choices.
- **TempData messages are serialized as JSON** into the script, not HTML-encoded
  into quotes. HTML encoding leaves backslash escapes like `\x3C` alone for
  JavaScript to decode. `JsonSerializer` escapes quotes, backslashes, line breaks and
  `<`, so the text can't end the string or the script tag.
- **The brand text is 0.8rem.** At 0.95 the name was 181px in a slot about 165px
  wide, ran through the padding and was clipped at the sidebar edge.

## Views/UnitTracker/Index.cshtml

- **The Where and Build boxes.** They're the two leftmost columns so they stay on
  screen when the table scrolls sideways on a phone. Bootstrap's white unchecked box
  and its faint border vanish on a white card, so the box is transparent with an edge
  above 3:1 in both themes. Checked is a solid blue that clears 3:1 against its own
  card: a lighter blue on the dark card, where the light-theme blue is only 2.6:1,
  and the deeper one on white.
- **They save in place.** A click posts and updates the row with no reload, so a long
  list keeps its scroll position. Each group allows one value at a time: ticking a
  box clears the others, ticking the lit one clears it, and blank means nobody has
  said. The boxes are disabled for Viewers, and the server refuses the post below
  Standard regardless.
- **A hidden Line or Team stays selectable in Edit.** A Line hidden in Settings since
  the row was saved isn't in the org structure, so it's added back to the select as
  "(current, hidden)". Without it the select would silently go blank and an
  untouched Save would fail. Teams get the same treatment.
- **Team sets Branch and Line, on purpose.** Picking a Team with a home Line
  overwrites both, because picking the Team is a deliberate act. A Team without one,
  or going back to N/A, changes nothing, and both stay editable afterwards.
- **The double-submit guard.** These forms post for real, and a second tap while the
  first is in flight would add the unit twice. The buttons are disabled only after
  the browser has built the form's entry list; disabling them sooner loses the
  clicked button's own name and value, which is how "Save and add another" is told
  apart from Save.
- **Toast text isn't escaped twice.** `showToast` sets the message as text, so
  escaping the server's text in the page as well would show "&amp;" on screen.

## wwwroot/css/site.css

- **Modal labels have one rule.** Every modal's label colour used to be hard-coded
  page by page. Two pages carried their own copy and Command Center never got one, so
  its labels rendered pure white. The modals are shared partials included on several
  pages, so the rule is global. `#E2E8F0` is about 1.17:1 against a white modal, so
  the light theme overrides it.
- **`.text-light-gray` has a real base colour.** It never had one: it inherited from
  whatever ancestor set `color`, usually the modal's white, which read as white, not
  light gray, and went invisible wherever an ancestor's background turned white in
  light theme. Three narrow overrides existed for single containers. The base rule is
  low specificity on purpose so the pinned-dark rows still win.
- **Registry rows stay dark in both themes.** The Compressor and Motor Registry rows
  and their filter bar keep an always-dark background, so the base rule would turn
  their light-gray text dark in light mode and make it unreadable. It's pinned back
  to a light gray there. The containers also get an explicit light colour as a safety
  net for plain descendant text.
- **Tables inside those rows** didn't inherit the dark surface. Bootstrap's `.table`
  paints every cell with `--bs-table-bg`, which resolved to white, so the light-gray
  location and date text sat on white at 2.6:1 and the yellow "(missing)" serial flag
  was 1.6:1, in both themes.
- **The contrast sweep (2026-09-24).** Measured, not eyeballed: every visible text
  node on every page, both themes, every modal, against WCAG AA (4.5:1 body, 3:1
  large), root causes first. What it changed:
  - Tables: nothing overrode Bootstrap's white cells, so in dark theme the Logs, All
    Items, Pickup Queue and Settings tables were white slabs, and each page's
    `.text-dim` cells sat on white at 2.6:1. Tables now take their card's surface and
    the app's text colour in both themes.
  - `.text-dim` is declared page-locally on five pages as `#94A3B8 !important`, fine
    on dark and 2.4 to 2.6:1 on light. One light-theme override outranks all five.
  - Page headings carried `.text-white` from the dark-only days: white on the light
    page, about 1:1.
  - Bootstrap's `.text-muted` is tuned for a white page, so "Cart is empty." was
    1.15:1 on the dark viewer.
  - Bootstrap's success green as text is 3.7:1 on the dark card, and 4.0:1 on outline
    buttons, short of 4.5 for 13px labels.
  - `--vis-teal` as button text is 4.0:1 on the dark card.
  - `.btn-info` was white on `--vis-blue`, 2.8:1 in both themes. A dark label is
    7.4:1.
  - Inset read-out boxes were pinned dark inline, so in light theme the teal label on
    them was 3.6:1.
- **Autocomplete rows use one class.** Each builder used to paint the row dark inline
  with a `.text-white` label, and in light theme the modal's rule turned that label
  dark, so the item name went dark-on-dark and only the blue id stayed readable.
  Hover is CSS now, not mouseover handlers.
- **`min-height: 100vh` on the body.** It fills the viewport so the fixed dot grid
  isn't clipped to a short body. The percentage min-height on the shared rule
  collapses because `html` has no definite height; `100vh` always resolves.
- **The dot grid scrolls on small screens.** `background-attachment: fixed` is
  unreliable on mobile Safari and Chrome: it desyncs the page's scroll from the fixed
  background layer. On a phone the dashboard wouldn't scroll and the grid moved by
  itself. The fixed grid is cosmetic, so below 1100px it drops to `scroll`.
- **How the light theme works.** `data-theme="light"` on `<html>` swaps the
  `--vis-*` variables. The brand accent colours stay the same. Everything in the
  light-theme section after that is a targeted fix for an element that hard-codes
  white text or a dark background and so ignores the swap. The sidebar and top bar
  switch to a light surface too, which the old always-dark navbar didn't.
- **Plain modal text had two gaps.** The base `.modal-content` rule had no
  light-theme override for its `color: white`, and most modal bodies also carry the
  literal `text-white` class, a Bootstrap utility with `!important` that beats a
  plain colour override. On a white modal that text was completely invisible:
  Confirm Final Order's body was a blank white box.
- **Tinted headers don't follow the theme.** Headers with `bg-primary`, `bg-dark`,
  `bg-danger`, `bg-success`, or `.modal-header-tinted` for one coloured inline, keep
  white text in light theme. The general rule had turned the Export Wizard's title
  dark on its dark header. `bg-warning` is excluded because it carries `.text-dark`.
- **Four selectors, on purpose.** The always-dark filter bar and registry rows sit
  inside modals too, so the light-theme modal rules would turn their labels and
  info or warning text dark-on-dark. Their exceptions use four class-level selectors
  to outrank the three-selector rules regardless of source order.
- **Bootstrap's dark-tuned colours on white.** `text-info` and `text-warning` read at
  about 1.6 to 2.0:1 on a white modal or card. `btn-outline-danger`, `-warning` and
  `-info` are about 2:1 on white. The fixes are sitewide because the same variants
  show up on My Activity, Pickup Queue, Intake and All Items.
- **The close button.** The base rule `.btn-close { filter: invert(1) }` flips
  Bootstrap's dark icon white, which is right on dark cards and exactly wrong once
  the card is white. Ten bare close buttons across five views were affected and only
  three had ever been fixed, individually, by id. It's fixed by surface, not by
  theme alone: a blanket light-theme override would also repaint the button on the
  tinted headers, which don't change with the theme, and put a dark icon on the blue
  header at about 3.6:1. Amber headers and alerts are light in both themes, so their
  button is dark in both; white-on-amber was about 1.9:1 in dark mode too, a bug
  nobody had reported.
- **`.modal-header-tinted`** is the hook for a header coloured by CSS and not a
  Bootstrap `bg-` class (New Item Registry paints itself green inline). Mark any
  future hand-tinted header with it instead of adding another id. A by-name list is
  what let the close-button bug sit unnoticed.
- **Sidebar brand and user name.** Both carried `.text-white` from when the bars were
  always dark. On a white surface only the red V, I and S stayed visible.
- **The app shell.** It touches every page because the layout wraps all of them. On
  desktop the sidebar is a persistent rail, collapsible to icons. Below 992px it's an
  off-canvas drawer over a backdrop, `position: fixed` so the page's own scroll
  can't affect it. Body scroll is deliberately not locked while it's open: one real
  mobile scroll bug had already come from a fixed-position and background
  interaction, and a second wasn't worth risking for a cosmetic nicety.
- **Shared card styles** lived in Command Center's own style block until Search
  Center's Actions row needed the same look. One definition for two pages.
- **`.btn-outline-light` in light theme.** Bootstrap builds it for dark backgrounds:
  the border is `#f8f9fa`, and a checked toggle fills with the same colour. On the
  dark cards that's 15.8:1. On a white card it's 1.05:1 in both states, so the
  button has no shape and a toggle pair gives no clue which half is selected. The
  label stayed readable, which is why it never looked obviously broken. Ten call
  sites use it, so it's fixed once in the stylesheet. Unchecked gets a real grey
  edge; checked or hovered gets a dark fill with white text, distinct from
  `.btn-outline-secondary`'s grey fill.
- **Reservations use violet.** Violet means "held" and no other VIS state uses it.
  The light-theme values are darker so text on white stays above 4.5:1. The Item
  Card's TC amber darkens in light theme for the same reason (2.0:1 otherwise).

## wwwroot/js/site.js

- **Load order.** The script tag sits at the top of the body so these helpers exist
  for page-inline scripts that run later. The sidebar wiring still waits for
  `DOMContentLoaded`, because `#appSidebar` renders further down the page: querying
  for it immediately returns null and the whole block silently does nothing. Waiting
  only delays running that block, not the parsing that defines the functions.
- **Sidebar collapse state** is a UI preference kept in localStorage, not a field on
  the user. It isn't worth a schema change.
- **`--vis-sidebar-width`.** The dashboard's overlay and selection grids are
  `position: fixed`, so they don't take part in the app shell's flex layout and sat
  under the sidebar instead of beside it. The variable is their hook back in, read
  only above the 992px breakpoint where the sidebar is a persistent rail.
- **The shared modal helpers read page data by name.** `itemsList`, `orgStructure`,
  `teamLines`, `rackRowMap`, `locDecodeMap` and `locMap` are declared by each calling
  page. They're resolved when a function is called, not when this file is parsed, so
  it's safe for the file to load before those constants exist.
- **One Branch to Line cascade.** Everywhere a Branch select drives a Line select
  uses the same functions over `orgStructure`, instead of each caller hand-rolling a
  copy. That's the failure the location vocabulary hit eight times over. The callers
  are the Ownership pane, New Item Registry and the compressor filter.
- **bindAutocomplete's two flags.** `selectable = false` is the New Item Registry
  case: matches show as a heads-up that something similar may exist, and clicking
  one never blocks typing a new name, since registering something new is the point
  of that form. `jumpToStock = true` takes it one step further: clicking a match
  means "this already exists, take me there", so it closes New Item and opens Modify
  Stock on that item, discarding what was typed.
- **Suggestion rows are built from DOM nodes, not HTML strings.** Item names and
  part numbers are typed by Standard-level users and shown in every other user's
  session.
- **bindValueAutocomplete.** Picking a suggestion sets the input and fires a real
  `input` event, so whatever is already listening (the id preview, the unit-capture
  toggle) still runs. That event is suppressed once so it doesn't reopen the list.
  The value source can be a function, evaluated on every keystroke, for lists that
  depend on what's picked right now (Rack and Row).
- **`opts.floating`** pins the list to the viewport under the input. It's needed
  wherever the input sits inside a clipping ancestor: Bulk Intake's rows live in a
  `.table-responsive`, whose horizontal overflow also clips vertically, so an
  absolutely positioned list got cut off at the table's bottom edge. The list opens
  upward when there isn't room below.
- **`opts.extraItem`** adds one visually distinct row after the matches, such as
  "Add as a new type". It shows even when nothing matches.
- **encodeLoc has to match the server.** It's the client half of the location code
  rule and must stay in step with `LocationCodec.Encode`.
- **A Razor trap worth remembering.** Inside a `.cshtml` file, an at-sign followed
  by a call is a Razor transition even inside a JavaScript comment, and it runs for
  real. A comment that named the layout's body-render call that way took the
  dashboard down once. Don't write one in a view outside a Razor `@* *@` comment,
  which is the only kind views use now.
