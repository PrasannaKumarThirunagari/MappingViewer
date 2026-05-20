// Column resize, reorder, and expand-modal table for .data-table elements.
(function (global) {
    'use strict';

    var MIN_COL_WIDTH = 72;
    var DEFAULT_COL_WIDTH = 140;
    var storagePrefix = 'mappingViewer.table.';

    function tableKey(table) {
        if (table.id) return table.id;
        if (!table.dataset.tableKey) {
            var block = table.closest('.table-block');
            var panel = table.closest('.sheet-panel');
            var sheet = panel ? panel.id : 'sheet';
            var idx = block ? block.dataset.tableIndex : '0';
            table.dataset.tableKey = sheet + '-' + idx;
        }
        return table.dataset.tableKey;
    }

    function loadState(key) {
        try {
            var raw = sessionStorage.getItem(storagePrefix + key);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    }

    function saveState(table) {
        var key = tableKey(table);
        var order = getColumnOrder(table);
        var widths = getColumnWidths(table);
        sessionStorage.setItem(storagePrefix + key, JSON.stringify({ order: order, widths: widths }));
    }

    function getColumnOrder(table) {
        var headerRow = table.querySelector('.data-table__headers');
        if (!headerRow) return [];
        return Array.from(headerRow.querySelectorAll('th[data-col-idx]')).map(function (th) {
            return parseInt(th.dataset.colIdx, 10);
        });
    }

    function getColumnWidths(table) {
        var headerRow = table.querySelector('.data-table__headers');
        var widths = {};
        if (!headerRow) return widths;
        headerRow.querySelectorAll('th[data-col-idx]').forEach(function (th) {
            var idx = parseInt(th.dataset.colIdx, 10);
            var w = th.style.width || th.getBoundingClientRect().width;
            widths[idx] = parseInt(w, 10) || DEFAULT_COL_WIDTH;
        });
        return widths;
    }

    function rowsWithCells(table) {
        var rows = [];
        var header = table.querySelector('.data-table__headers');
        var filters = table.querySelector('.data-table__filters');
        if (header) rows.push(header);
        if (filters) rows.push(filters);
        table.querySelectorAll('tbody tr').forEach(function (tr) { rows.push(tr); });
        return rows;
    }

    function getCell(row, colIdx) {
        return row.querySelector('[data-col-idx="' + colIdx + '"]');
    }

    function applyColumnOrder(table, order) {
        rowsWithCells(table).forEach(function (row) {
            var fragment = document.createDocumentFragment();
            order.forEach(function (colIdx) {
                var cell = getCell(row, colIdx);
                if (cell) fragment.appendChild(cell);
            });
            row.appendChild(fragment);
        });
        reorderColgroup(table, order);
        syncFilterColAttributes(table, order);
    }

    function syncFilterColAttributes(table, order) {
        var filterRow = table.querySelector('.data-table__filters');
        if (!filterRow) return;
        var inputs = filterRow.querySelectorAll('.col-filter');
        order.forEach(function (colIdx, visualPos) {
            var input = Array.from(inputs).find(function (inp) {
                return parseInt(inp.dataset.col, 10) === colIdx;
            });
            if (input) {
                input.dataset.visualCol = String(visualPos);
            }
        });
    }

    function ensureColgroup(table) {
        var colgroup = table.querySelector('colgroup');
        if (!colgroup) {
            colgroup = document.createElement('colgroup');
            table.insertBefore(colgroup, table.firstChild);
        }
        if (colgroup.children.length === 0) {
            var header = table.querySelector('.data-table__headers');
            if (header) {
                header.querySelectorAll('th[data-col-idx]').forEach(function (th) {
                    var col = document.createElement('col');
                    col.dataset.colIdx = th.dataset.colIdx;
                    colgroup.appendChild(col);
                });
            }
        }
        return colgroup;
    }

    function reorderColgroup(table, order) {
        var colgroup = ensureColgroup(table);
        var cols = {};
        colgroup.querySelectorAll('col').forEach(function (col) {
            cols[col.dataset.colIdx] = col;
        });
        colgroup.innerHTML = '';
        order.forEach(function (idx) {
            if (cols[idx]) colgroup.appendChild(cols[idx]);
        });
    }

    function applyColumnWidths(table, widths) {
        ensureColgroup(table);
        Object.keys(widths).forEach(function (idx) {
            var w = Math.max(MIN_COL_WIDTH, widths[idx]) + 'px';
            table.querySelectorAll('[data-col-idx="' + idx + '"]').forEach(function (cell) {
                cell.style.width = w;
                cell.style.minWidth = w;
                cell.style.maxWidth = w;
            });
            var col = table.querySelector('colgroup col[data-col-idx="' + idx + '"]');
            if (col) col.style.width = w;
        });
    }

    function resetTable(table) {
        var indices = [];
        table.querySelectorAll('.data-table__headers th[data-col-idx]').forEach(function (th) {
            indices.push(parseInt(th.dataset.colIdx, 10));
        });
        var natural = indices.slice().sort(function (a, b) { return a - b; });
        applyColumnOrder(table, natural);
        table.querySelectorAll('[data-col-idx]').forEach(function (cell) {
            cell.style.width = '';
            cell.style.minWidth = '';
            cell.style.maxWidth = '';
        });
        table.querySelectorAll('colgroup col').forEach(function (col) {
            col.style.width = '';
        });
        sessionStorage.removeItem(storagePrefix + tableKey(table));
    }

    function initResize(table) {
        table.querySelectorAll('.col-resize-handle').forEach(function (handle) {
            handle.addEventListener('mousedown', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var th = handle.closest('th');
                if (!th) return;
                var colIdx = th.dataset.colIdx;
                var startX = e.clientX;
                var startW = th.getBoundingClientRect().width;

                table.classList.add('data-table--resizing');
                document.body.style.cursor = 'col-resize';
                document.body.style.userSelect = 'none';

                function onMove(ev) {
                    var w = Math.max(MIN_COL_WIDTH, startW + (ev.clientX - startX));
                    var px = w + 'px';
                    table.querySelectorAll('[data-col-idx="' + colIdx + '"]').forEach(function (cell) {
                        cell.style.width = px;
                        cell.style.minWidth = px;
                        cell.style.maxWidth = px;
                    });
                    var col = table.querySelector('colgroup col[data-col-idx="' + colIdx + '"]');
                    if (col) col.style.width = px;
                }

                function onUp() {
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    table.classList.remove('data-table--resizing');
                    document.body.style.cursor = '';
                    document.body.style.userSelect = '';
                    saveState(table);
                    if (global.MappingViewer && global.MappingViewer.markTruncatedCells) {
                        global.MappingViewer.markTruncatedCells();
                    }
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
            });
        });
    }

    function initReorder(table) {
        var dragColIdx = null;
        table.querySelectorAll('.data-table__headers th[data-col-idx]').forEach(function (th) {
            th.addEventListener('dragstart', function (e) {
                if (e.target.closest('.col-resize-handle')) {
                    e.preventDefault();
                    return;
                }
                dragColIdx = th.dataset.colIdx;
                th.classList.add('th--dragging');
                e.dataTransfer.effectAllowed = 'move';
                e.dataTransfer.setData('text/plain', dragColIdx);
            });
            th.addEventListener('dragend', function () {
                th.classList.remove('th--dragging');
                table.querySelectorAll('.th--drag-over').forEach(function (el) {
                    el.classList.remove('th--drag-over');
                });
            });
            th.addEventListener('dragover', function (e) {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                th.classList.add('th--drag-over');
            });
            th.addEventListener('dragleave', function () {
                th.classList.remove('th--drag-over');
            });
            th.addEventListener('drop', function (e) {
                e.preventDefault();
                th.classList.remove('th--drag-over');
                var fromIdx = dragColIdx;
                var toIdx = th.dataset.colIdx;
                if (fromIdx == null || toIdx == null || fromIdx === toIdx) return;
                var order = getColumnOrder(table);
                var fromPos = order.indexOf(parseInt(fromIdx, 10));
                var toPos = order.indexOf(parseInt(toIdx, 10));
                if (fromPos < 0 || toPos < 0) return;
                order.splice(fromPos, 1);
                order.splice(toPos, 0, parseInt(fromIdx, 10));
                applyColumnOrder(table, order);
                saveState(table);
                clearRowCache(table);
                if (global.MappingViewer && global.MappingViewer.applyFiltersToTable) {
                    global.MappingViewer.applyFiltersToTable(table, global.MappingViewer.getGlobalState());
                }
                if (global.MappingViewer && global.MappingViewer.markTruncatedCells) {
                    global.MappingViewer.markTruncatedCells();
                }
            });
        });
    }

    function clearRowCache(table) {
        table.querySelectorAll('tbody tr').forEach(function (row) {
            delete row.__cellsCS;
            delete row.__cellsCI;
            delete row.__joinedCS;
            delete row.__joinedCI;
        });
    }

    function initTable(table) {
        if (table.dataset.gridInit === '1') return;
        table.dataset.gridInit = '1';

        var key = tableKey(table);
        var saved = loadState(key);
        if (saved && saved.order && saved.order.length) {
            applyColumnOrder(table, saved.order);
        }
        if (saved && saved.widths) {
            applyColumnWidths(table, saved.widths);
        }

        initResize(table);
        initReorder(table);
    }

    function initAll(root) {
        (root || document).querySelectorAll('table.data-table').forEach(initTable);
    }

    function openExpandModal(table) {
        var modal = document.getElementById('tableExpandModal');
        var body = document.getElementById('tableExpandBody');
        var titleEl = document.getElementById('tableExpandTitle');
        if (!modal || !body) return;

        body.innerHTML = '';
        var block = table.closest('.table-block');
        var titleBar = block ? block.querySelector('.table-block__title-text') : null;
        titleEl.textContent = titleBar ? titleBar.textContent.trim() : 'Table';

        var wrap = document.createElement('div');
        wrap.className = 'table-expand-scroll';
        var clone = table.cloneNode(true);
        clone.removeAttribute('id');
        clone.classList.add('data-table--expanded');
        clone.dataset.gridInit = '';
        wrap.appendChild(clone);
        body.appendChild(wrap);

        initTable(clone);
        modal.classList.remove('is-hidden');
        document.body.classList.add('modal-open');

        var searchInput = document.getElementById('tableExpandSearch');
        var caseInput = document.getElementById('tableExpandCase');
        function applyExpandFilters() {
            if (global.MappingViewer && global.MappingViewer.applyFiltersToTable) {
                var state = global.MappingViewer.getGlobalStateForExpand
                    ? global.MappingViewer.getGlobalStateForExpand()
                    : { caseSensitive: false, globalTerm: '' };
                global.MappingViewer.applyFiltersToTable(clone, state);
            }
        }

        if (searchInput) {
            searchInput.value = '';
            searchInput.oninput = function () {
                global.MappingViewer.clearRowTextCache(clone);
                applyExpandFilters();
            };
            setTimeout(function () { searchInput.focus(); }, 50);
        }
        if (caseInput) {
            caseInput.checked = document.getElementById('caseSensitive')
                ? document.getElementById('caseSensitive').checked
                : false;
            caseInput.onchange = function () {
                global.MappingViewer.clearRowTextCache(clone);
                applyExpandFilters();
            };
        }

        clone.querySelectorAll('.col-filter').forEach(function (input) {
            input.addEventListener('input', function () {
                global.MappingViewer.clearRowTextCache(clone);
                applyExpandFilters();
            });
        });

        applyExpandFilters();
        if (global.MappingViewer && global.MappingViewer.markTruncatedCells) {
            global.MappingViewer.markTruncatedCells();
        }
    }

    function closeExpandModal() {
        var modal = document.getElementById('tableExpandModal');
        if (!modal) return;
        modal.classList.add('is-hidden');
        document.body.classList.remove('modal-open');
        var body = document.getElementById('tableExpandBody');
        if (body) body.innerHTML = '';
    }

    global.TableGrid = {
        init: initTable,
        initAll: initAll,
        reset: resetTable,
        openExpand: openExpandModal,
        closeExpand: closeExpandModal,
        applyColumnOrder: applyColumnOrder,
        getColumnOrder: getColumnOrder
    };
})(window);
