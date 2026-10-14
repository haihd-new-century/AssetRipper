// Asset browser: live filtering, class filter and grid/table views for collection pages.
(function () {
	'use strict';

	const VIEW_KEY = 'assetripper.view';

	function ready(fn) {
		if (document.readyState !== 'loading') { fn(); } else { document.addEventListener('DOMContentLoaded', fn); }
	}

	ready(function () {
		const grid = document.getElementById('assetGrid');
		const tableWrapper = document.getElementById('assetTableWrapper');
		if (!grid || !tableWrapper) { return; }

		const search = document.getElementById('assetFilter');
		const classFilter = document.getElementById('classFilter');
		const counter = document.getElementById('filterCount');
		const empty = document.getElementById('emptyFilterMessage');
		const cards = Array.from(grid.querySelectorAll('[data-search]'));
		const rows = Array.from(document.querySelectorAll('#assetsTable tr[data-search]'));
		const buttons = Array.from(document.querySelectorAll('[data-view]'));
		const total = cards.length;

		// Table thumbnails are only loaded once the table is shown.
		function loadTableThumbnails() {
			tableWrapper.querySelectorAll('img[data-src]').forEach(function (img) {
				img.src = img.getAttribute('data-src');
				img.removeAttribute('data-src');
			});
		}

		function setView(view) {
			const table = view === 'table';
			grid.classList.toggle('d-none', table);
			tableWrapper.classList.toggle('d-none', !table);
			buttons.forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-view') === view); });
			if (table) { loadTableThumbnails(); }
			try { localStorage.setItem(VIEW_KEY, view); } catch (e) { /* private mode */ }
		}

		function applyFilter() {
			const text = (search ? search.value : '').trim().toLowerCase();
			const cls = classFilter ? classFilter.value : '';
			let shown = 0;
			for (let i = 0; i < cards.length; i++) {
				const card = cards[i];
				const visible = (!cls || card.getAttribute('data-class') === cls)
					&& (!text || card.getAttribute('data-search').indexOf(text) !== -1);
				card.classList.toggle('d-none', !visible);
				if (rows[i]) { rows[i].classList.toggle('d-none', !visible); }
				if (visible) { shown++; }
			}
			if (counter) {
				const template = counter.getAttribute('data-template') || '{0} / {1}';
				counter.textContent = template.replace('{0}', shown.toLocaleString()).replace('{1}', total.toLocaleString());
			}
			if (empty) { empty.classList.toggle('d-none', shown !== 0); }
		}

		let timer = 0;
		if (search) {
			search.addEventListener('input', function () {
				clearTimeout(timer);
				timer = setTimeout(applyFilter, 120);
			});
			// "/" focuses the filter, like many code browsers.
			document.addEventListener('keydown', function (e) {
				if (e.key === '/' && document.activeElement !== search && !(e.target instanceof HTMLInputElement)) {
					e.preventDefault();
					search.focus();
				}
			});
		}
		if (classFilter) { classFilter.addEventListener('change', applyFilter); }
		buttons.forEach(function (b) {
			b.addEventListener('click', function () { setView(b.getAttribute('data-view')); });
		});

		let saved = 'grid';
		try { saved = localStorage.getItem(VIEW_KEY) || 'grid'; } catch (e) { /* ignore */ }
		setView(saved);
		applyFilter();
	});
})();
