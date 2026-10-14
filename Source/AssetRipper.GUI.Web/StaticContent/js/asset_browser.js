// Asset browser for collection pages: live filtering, class filter, grid/table views,
// incremental rendering and lazy thumbnails (only requested when scrolled into view).
(function () {
	'use strict';

	const VIEW_KEY = 'assetripper.view';
	const PAGE_SIZE = 240;

	function ready(fn) {
		if (document.readyState !== 'loading') { fn(); } else { document.addEventListener('DOMContentLoaded', fn); }
	}

	ready(function () {
		const grid = document.getElementById('assetGrid');
		const tableWrapper = document.getElementById('assetTableWrapper');
		if (!grid || !tableWrapper) { return; }

		const tableBody = document.getElementById('assetsTable');
		const search = document.getElementById('assetFilter');
		const classFilter = document.getElementById('classFilter');
		const counter = document.getElementById('filterCount');
		const empty = document.getElementById('emptyFilterMessage');
		const showMore = document.getElementById('showMore');
		const buttons = Array.from(document.querySelectorAll('[data-view]'));
		const cards = Array.from(grid.querySelectorAll('.ar-card'));
		const total = cards.length;

		let view = 'grid';
		let matches = cards;
		let limit = PAGE_SIZE;

		// Lazy thumbnails.
		const observer = 'IntersectionObserver' in window
			? new IntersectionObserver(function (entries) {
				entries.forEach(function (entry) {
					if (entry.isIntersecting) {
						const img = entry.target;
						img.src = img.getAttribute('data-src');
						img.removeAttribute('data-src');
						observer.unobserve(img);
					}
				});
			}, { rootMargin: '400px' })
			: null;

		function watch(img) {
			if (!img || !img.hasAttribute('data-src')) { return; }
			if (observer) { observer.observe(img); } else { img.src = img.getAttribute('data-src'); }
		}

		function el(tag, className, text) {
			const e = document.createElement(tag);
			if (className) { e.className = className; }
			if (text !== undefined) { e.textContent = text; }
			return e;
		}

		function buildRow(card) {
			const tr = document.createElement('tr');
			const kind = card.getAttribute('data-kind') || 'other';

			const thumbCell = el('td', 'ar-col-thumb');
			const thumb = card.querySelector('.ar-thumb img');
			if (thumb) {
				const img = el('img', 'ar-mini-thumb');
				img.alt = '';
				img.setAttribute('data-src', thumb.getAttribute('data-src') || thumb.src);
				thumbCell.appendChild(img);
				watch(img);
			} else {
				const icon = card.querySelector('.ar-thumb-icon');
				thumbCell.appendChild(el('span', 'ar-mini-icon', icon ? icon.textContent : '📦'));
			}
			tr.appendChild(thumbCell);

			const nameCell = el('td');
			const link = el('a', '', card.querySelector('.ar-card-title').textContent);
			link.href = card.getAttribute('href');
			nameCell.appendChild(link);
			tr.appendChild(nameCell);

			const classCell = el('td');
			classCell.appendChild(el('span', 'badge ar-badge kind-' + kind, card.getAttribute('data-class')));
			tr.appendChild(classCell);

			tr.appendChild(el('td', 'text-secondary small', card.querySelector('.ar-card-detail').textContent));
			tr.appendChild(el('td', 'text-end font-monospace small', card.getAttribute('data-pathid')));
			return tr;
		}

		function render() {
			const visible = matches.slice(0, limit);
			if (view === 'grid') {
				const shown = new Set(visible);
				cards.forEach(function (card) { card.classList.toggle('d-none', !shown.has(card)); });
				visible.forEach(function (card) { watch(card.querySelector('.ar-thumb img')); });
			} else {
				tableBody.replaceChildren.apply(tableBody, visible.map(buildRow));
			}
			if (showMore) {
				const remaining = matches.length - visible.length;
				showMore.classList.toggle('d-none', remaining <= 0);
				showMore.textContent = '+ ' + Math.min(remaining, PAGE_SIZE).toLocaleString() + ' / ' + remaining.toLocaleString();
			}
			if (counter) {
				const template = counter.getAttribute('data-template') || '{0} / {1}';
				counter.textContent = template.replace('{0}', matches.length.toLocaleString()).replace('{1}', total.toLocaleString());
			}
			if (empty) { empty.classList.toggle('d-none', matches.length !== 0); }
		}

		function applyFilter() {
			const text = (search ? search.value : '').trim().toLowerCase();
			const cls = classFilter ? classFilter.value : '';
			matches = cards.filter(function (card) {
				return (!cls || card.getAttribute('data-class') === cls)
					&& (!text || card.getAttribute('data-search').indexOf(text) !== -1);
			});
			limit = PAGE_SIZE;
			render();
		}

		function setView(next) {
			view = next === 'table' ? 'table' : 'grid';
			grid.classList.toggle('d-none', view === 'table');
			tableWrapper.classList.toggle('d-none', view !== 'table');
			buttons.forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-view') === view); });
			try { localStorage.setItem(VIEW_KEY, view); } catch (e) { /* private mode */ }
			render();
		}

		let timer = 0;
		if (search) {
			search.addEventListener('input', function () {
				clearTimeout(timer);
				timer = setTimeout(applyFilter, 120);
			});
			document.addEventListener('keydown', function (e) {
				if (e.key === '/' && document.activeElement !== search && !(e.target instanceof HTMLInputElement)) {
					e.preventDefault();
					search.focus();
				}
			});
		}
		if (classFilter) { classFilter.addEventListener('change', applyFilter); }
		if (showMore) {
			showMore.addEventListener('click', function () {
				limit += PAGE_SIZE;
				render();
			});
		}
		buttons.forEach(function (b) {
			b.addEventListener('click', function () { setView(b.getAttribute('data-view')); });
		});

		let saved = 'grid';
		try { saved = localStorage.getItem(VIEW_KEY) || 'grid'; } catch (e) { /* ignore */ }
		matches = cards;
		setView(saved);
	});
})();
