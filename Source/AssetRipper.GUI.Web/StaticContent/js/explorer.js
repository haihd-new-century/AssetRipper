// Two-pane game explorer: folder tree on the left, folder contents + item preview on the right.
(function () {
	'use strict';

	const LAYOUT_KEY = 'assetripper.explorer.layout';
	const PAGE_SIZE = 300;

	function ready(fn) {
		if (document.readyState !== 'loading') { fn(); } else { document.addEventListener('DOMContentLoaded', fn); }
	}

	ready(function () {
		const root = document.querySelector('.ar-explorer');
		if (!root) { return; }
		const content = document.getElementById('explorerContent');
		const preview = document.getElementById('explorerPreview');
		const tree = root.querySelector('.ar-tree');
		const treeFilter = root.querySelector('.ar-tree-filter');
		const emptyPreview = preview.innerHTML;
		let requestId = 0;

		const observer = 'IntersectionObserver' in window
			? new IntersectionObserver(function (entries) {
				entries.forEach(function (e) {
					if (e.isIntersecting) {
						const img = e.target;
						img.src = img.getAttribute('data-src');
						img.removeAttribute('data-src');
						observer.unobserve(img);
					}
				});
			}, { root: content, rootMargin: '300px' })
			: null;
		function watch(img) {
			if (!img || !img.hasAttribute('data-src')) { return; }
			if (observer) { observer.observe(img); } else { img.src = img.getAttribute('data-src'); }
		}

		// ---------- Tree ----------
		function selectRow(url) {
			tree.querySelectorAll('.ar-node-row.selected').forEach(function (r) { r.classList.remove('selected'); });
			const row = tree.querySelector('.ar-node-row[data-explorer-url="' + CSS.escape(url) + '"]');
			if (row) {
				row.classList.add('selected');
				for (let n = row.parentElement; n && n !== tree; n = n.parentElement) {
					if (n.classList && n.classList.contains('ar-node')) { n.classList.add('open'); }
				}
				row.scrollIntoView({ block: 'nearest' });
			}
		}

		tree.addEventListener('click', function (e) {
			const caret = e.target.closest('.ar-caret');
			const row = e.target.closest('.ar-node-row');
			if (!row) { return; }
			if (caret && !caret.classList.contains('empty')) {
				row.parentElement.classList.toggle('open');
				return;
			}
			const url = row.getAttribute('data-explorer-url');
			if (url) {
				e.preventDefault();
				navigate(url, true);
			}
		});
		tree.addEventListener('dblclick', function (e) {
			const row = e.target.closest('.ar-node-row');
			if (row && row.querySelector('.ar-caret:not(.empty)')) { row.parentElement.classList.toggle('open'); }
		});

		if (treeFilter) {
			treeFilter.addEventListener('input', function () {
				const text = treeFilter.value.trim().toLowerCase();
				tree.querySelectorAll('.ar-node').forEach(function (node) {
					if (!text) { node.classList.remove('d-none'); return; }
					const match = Array.from(node.querySelectorAll('.ar-node-row')).some(function (r) {
						return (r.getAttribute('data-search') || '').indexOf(text) !== -1;
					});
					node.classList.toggle('d-none', !match);
					if (match) { node.classList.add('open'); }
				});
			});
		}

		// ---------- Content ----------
		async function navigate(url, push) {
			const id = ++requestId;
			selectRow(url);
			content.classList.add('ar-busy');
			try {
				const response = await fetch(url, { headers: { 'Accept': 'text/html' } });
				const html = await response.text();
				if (id !== requestId) { return; }
				content.innerHTML = response.ok ? html : '<p class="text-danger p-4"></p>';
				if (!response.ok) { content.firstChild.textContent = html; }
				content.scrollTop = 0;
				preview.innerHTML = emptyPreview;
				initContent();
				if (push) { history.pushState({ explorer: url }, '', '#' + encodeURIComponent(url)); }
			} catch (err) {
				if (id === requestId) { content.innerHTML = '<p class="text-danger p-4">' + String(err) + '</p>'; }
			} finally {
				if (id === requestId) { content.classList.remove('ar-busy'); }
			}
		}

		// Folder tiles and breadcrumbs navigate inside the explorer.
		content.addEventListener('click', function (e) {
			const nav = e.target.closest('[data-explorer-url]');
			if (nav && !e.ctrlKey && !e.metaKey) {
				e.preventDefault();
				navigate(nav.getAttribute('data-explorer-url'), true);
				return;
			}
			const item = e.target.closest('.ar-item[data-preview-url]');
			if (item && !e.ctrlKey && !e.metaKey) {
				// Single click selects and previews; double click (or Enter) opens.
				e.preventDefault();
				select(item);
			}
		});
		content.addEventListener('dblclick', function (e) {
			const item = e.target.closest('.ar-item[data-preview-url]');
			if (item) { window.location.href = item.getAttribute('href'); }
		});
		content.addEventListener('keydown', function (e) {
			const item = e.target.closest('.ar-item[data-preview-url]');
			if (!item) { return; }
			if (e.key === 'Enter') { window.location.href = item.getAttribute('href'); }
			if (e.key === 'ArrowDown' || e.key === 'ArrowRight' || e.key === 'ArrowUp' || e.key === 'ArrowLeft') {
				const items = Array.from(content.querySelectorAll('.ar-item[data-preview-url]:not(.d-none)'));
				const index = items.indexOf(item) + (e.key === 'ArrowDown' || e.key === 'ArrowRight' ? 1 : -1);
				if (items[index]) { e.preventDefault(); select(items[index]); items[index].focus(); }
			}
		});

		async function select(item) {
			content.querySelectorAll('.ar-item.selected').forEach(function (i) { i.classList.remove('selected'); });
			item.classList.add('selected');
			preview.classList.add('ar-busy');
			try {
				const response = await fetch(item.getAttribute('data-preview-url'));
				if (item.classList.contains('selected')) { preview.innerHTML = await response.text(); }
			} finally {
				preview.classList.remove('ar-busy');
			}
		}

		function initContent() {
			const items = content.querySelector('.ar-items');
			if (!items) { return; }
			const all = Array.from(items.querySelectorAll('.ar-item[data-preview-url]'));
			const filter = content.querySelector('.ar-explorer-filter');
			const classFilter = content.querySelector('.ar-explorer-class');
			const counter = content.querySelector('.ar-explorer-count');
			const more = content.querySelector('.ar-explorer-more');
			const layoutButtons = Array.from(content.querySelectorAll('[data-layout]'));
			let matches = all;
			let limit = PAGE_SIZE;

			function render() {
				const shown = new Set(matches.slice(0, limit));
				all.forEach(function (item) {
					const visible = shown.has(item);
					item.classList.toggle('d-none', !visible);
					if (visible) { watch(item.querySelector('img[data-src]')); }
				});
				const remaining = matches.length - shown.size;
				more.classList.toggle('d-none', remaining <= 0);
				more.textContent = '+ ' + Math.min(remaining, PAGE_SIZE).toLocaleString() + ' / ' + remaining.toLocaleString();
				const template = counter.getAttribute('data-template') || '{0} / {1}';
				counter.textContent = template.replace('{0}', matches.length.toLocaleString()).replace('{1}', all.length.toLocaleString());
			}
			function apply() {
				const text = filter.value.trim().toLowerCase();
				const cls = classFilter.value;
				matches = all.filter(function (item) {
					return (!cls || item.getAttribute('data-class') === cls)
						&& (!text || item.getAttribute('data-search').indexOf(text) !== -1);
				});
				limit = PAGE_SIZE;
				render();
			}
			function setLayout(layout) {
				items.classList.toggle('layout-icons', layout !== 'details');
				items.classList.toggle('layout-details', layout === 'details');
				layoutButtons.forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-layout') === layout); });
				try { localStorage.setItem(LAYOUT_KEY, layout); } catch (e) { /* ignore */ }
			}

			let timer = 0;
			filter.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(apply, 120); });
			classFilter.addEventListener('change', apply);
			more.addEventListener('click', function () { limit += PAGE_SIZE; render(); });
			layoutButtons.forEach(function (b) { b.addEventListener('click', function () { setLayout(b.getAttribute('data-layout')); }); });
			let saved = 'icons';
			try { saved = localStorage.getItem(LAYOUT_KEY) || 'icons'; } catch (e) { /* ignore */ }
			setLayout(saved);
			render();
		}

		window.addEventListener('popstate', function (e) {
			if (e.state && e.state.explorer) { navigate(e.state.explorer, false); }
		});

		const fromHash = location.hash.length > 1 ? decodeURIComponent(location.hash.slice(1)) : null;
		const start = fromHash && fromHash.indexOf('/Explorer/') === 0 ? fromHash : root.getAttribute('data-initial-url');
		history.replaceState({ explorer: start }, '', location.hash.length > 1 ? location.hash : '#' + encodeURIComponent(start));
		navigate(start, false);
	});
})();
