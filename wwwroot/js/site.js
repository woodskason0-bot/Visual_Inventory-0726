// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// App shell: sidebar collapse (desktop) + off-canvas drawer (mobile).
// Collapse state is client-side only (localStorage), not a User field --
// a UI preference, not worth a schema change yet.
(function () {
    var sidebar = document.getElementById('appSidebar');
    if (!sidebar) return;

    var backdrop = document.getElementById('sidebarBackdrop');
    var collapseBtn = document.getElementById('sidebarCollapseBtn');
    var collapseIcon = document.getElementById('sidebarCollapseIcon');
    var collapseLabel = document.getElementById('sidebarCollapseLabel');
    var hamburger = document.getElementById('sidebarHamburger');

    function applyCollapsedUI(collapsed) {
        if (collapseIcon) collapseIcon.className = collapsed ? 'bi bi-chevron-double-right' : 'bi bi-chevron-double-left';
        if (collapseLabel) collapseLabel.textContent = collapsed ? 'Expand' : 'Collapse';
    }

    var startCollapsed = localStorage.getItem('vis-sidebar-collapsed') === '1';
    if (startCollapsed) {
        sidebar.classList.add('collapsed');
        applyCollapsedUI(true);
    }

    if (collapseBtn) {
        collapseBtn.addEventListener('click', function () {
            var collapsed = sidebar.classList.toggle('collapsed');
            localStorage.setItem('vis-sidebar-collapsed', collapsed ? '1' : '0');
            applyCollapsedUI(collapsed);
        });
    }

    function openMobileSidebar() {
        sidebar.classList.add('mobile-open');
        if (backdrop) backdrop.classList.add('show');
    }
    function closeMobileSidebar() {
        sidebar.classList.remove('mobile-open');
        if (backdrop) backdrop.classList.remove('show');
    }

    if (hamburger) hamburger.addEventListener('click', openMobileSidebar);
    if (backdrop) backdrop.addEventListener('click', closeMobileSidebar);
    sidebar.querySelectorAll('.sidebar-link').forEach(function (a) {
        a.addEventListener('click', closeMobileSidebar);
    });
})();
