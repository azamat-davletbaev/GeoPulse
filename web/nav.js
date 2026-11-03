'use strict';

// Единая шапка: активный пункт меню определяется по location.pathname
// (не захардкожен в разметке). Подключается на всех четырёх страницах.
(function () {
  const path = window.location.pathname;
  const links = document.querySelectorAll('.topnav a[href]');

  for (const link of links) {
    const href = link.getAttribute('href');
    if (href === path) {
      link.classList.add('active');
      link.setAttribute('aria-current', 'page');
    }
  }
})();
