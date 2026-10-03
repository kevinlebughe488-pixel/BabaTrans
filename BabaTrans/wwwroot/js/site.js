// BABA-Trans : scripts communs à toutes les pages.

(function () {
    // Ouverture / fermeture de la barre latérale sur mobile
    const sidebarToggle = document.getElementById('sidebarToggle');
    const sidebarMenu = document.getElementById('sidebarMenu');
    if (sidebarToggle && sidebarMenu) {
        sidebarToggle.addEventListener('click', () => sidebarMenu.classList.toggle('show'));
        document.addEventListener('click', (e) => {
            if (sidebarMenu.classList.contains('show')
                && !sidebarMenu.contains(e.target)
                && !sidebarToggle.contains(e.target)) {
                sidebarMenu.classList.remove('show');
            }
        });
    }

    // Confirmation avant une action sensible : <form data-confirm="Message ?">
    document.querySelectorAll('form[data-confirm]').forEach((form) => {
        form.addEventListener('submit', (e) => {
            if (!window.confirm(form.dataset.confirm)) {
                e.preventDefault();
            }
        });
    });

    // Empêche le double envoi d'un formulaire (double clic sur « Valider »)
    document.querySelectorAll('form[method="post"]').forEach((form) => {
        form.addEventListener('submit', (e) => {
            if (e.defaultPrevented) return;
            if (window.jQuery && jQuery(form).data('validator') && !jQuery(form).valid()) return;
            form.querySelectorAll('button[type="submit"]').forEach((btn) => {
                setTimeout(() => { btn.disabled = true; }, 0);
            });
        });
    });
})();
