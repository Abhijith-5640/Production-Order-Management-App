import React, { useState, useEffect } from 'react';
import { X, Ban } from 'lucide-react';

const AdjustmentModal = ({
    isOpen,
    item,
    currentTrip,
    mode, // 'full_exclude' | 'single_exclude' | 'quantity_reduction'
    filterPurSaleId = null, // for single_exclude — restrict to one row
    onClose,
    onConfirm,
}) => {
    const [purSaleEntries, setPurSaleEntries] = useState([]);
    const [localAdjustments, setLocalAdjustments] = useState({});

    useEffect(() => {
        if (!isOpen || !item || !Array.isArray(item.distribution) || item.distribution.length === 0) {
            setPurSaleEntries([]);
            setLocalAdjustments({});
            return;
        }

        // Determine which entries to show based on mode
        let entries = item.distribution;
        if (mode === 'single_exclude' && filterPurSaleId != null) {
            entries = item.distribution.filter(d => d.purSaleId === filterPurSaleId);
        } else if (mode === 'quantity_reduction') {
            entries = item.distribution.filter(d => Number(d.qty) < Number(d.originalQty ?? d.qty));
        }
        setPurSaleEntries(entries);

        // Seed localAdjustments. Each row's target-trip dropdown is sourced
        // from its own entry.availableTrips (server-curated per PurTmpltId),
        // excluding the row's own trip. The "no candidates" guard that gated
        // the exclude checkbox is now evaluated per-row inside the render
        // (see `candidates` below) instead of via shared state.
        const initial = {};
        entries.forEach(d => {
            const candidates = (d.availableTrips ?? []).filter(t => t.id !== d.trip);
            initial[d.purSaleId] = {
                // For FE / SE: default to fully excluded
                // For QR: default to "move diff to next available trip" if one exists,
                //         else ignore (exclude = true means ignore the diff)
                exclude: mode === 'full_exclude' || mode === 'single_exclude' || candidates.length === 0,
                targetTrip: candidates.length > 0 ? candidates[0].id : ''
            };
        });
        setLocalAdjustments(initial);
    }, [isOpen, item, currentTrip, mode, filterPurSaleId]);

    if (!isOpen || !item) return null;

    const handleToggleExclude = (purSaleId) => {
        // Look up the row's own availableTrips to decide if toggling is allowed.
        const row = purSaleEntries.find(p => p.purSaleId === purSaleId);
        const candidates = (row?.availableTrips ?? []).filter(t => t.id !== row?.trip);
        if (candidates.length === 0) return; // Must remain excluded if no other trips
        setLocalAdjustments(prev => ({
            ...prev,
            [purSaleId]: {
                ...prev[purSaleId],
                exclude: !prev[purSaleId]?.exclude
            }
        }));
    };

    const handleChangeTargetTrip = (purSaleId, tripName) => {
        setLocalAdjustments(prev => ({
            ...prev,
            [purSaleId]: {
                ...prev[purSaleId],
                targetTrip: tripName
            }
        }));
    };

    const handleSave = () => {
        const updates = purSaleEntries.map(entry => {
            const config = localAdjustments[entry.purSaleId] || {};
            const isQtyReduction = mode === 'quantity_reduction';
            const reducedQty = isQtyReduction
                ? Number(entry.originalQty ?? entry.qty) - Number(entry.qty)
                : Number(entry.qty);
            return {
                purSaleId: entry.purSaleId,
                branch: entry.branch,
                qty: entry.qty,
                reducedQty: isQtyReduction ? reducedQty : null,
                // For QR: "exclude" on the diff means ignore the diff (no move, no discard)
                // For FE/SE: "exclude" means fully exclude this row
                balanceAction: isQtyReduction
                    ? (config.exclude ? 'ignore' : 'move')
                    : (config.exclude ? 'discard' : 'move'),
                targetTrip: config.targetTrip || null
            };
        });
        onConfirm(updates);
    };

    // Determine titles and subtitles based on mode
    let title = "Exclude Item";
    let subtitle = "Configure trip rollover or exclusion details";
    if (mode === 'quantity_reduction') {
        title = "Balance Quantity Rollover";
        subtitle = "Choose where to route the remaining balance quantities";
    } else if (mode === 'single_exclude') {
        title = "Exclude Branch";
        subtitle = "Choose whether to discard this branch or move it to a later trip";
    }

    const tripLabel = currentTrip ? (currentTrip.trip || currentTrip.name || '') : '';

    return (
        <div className="fixed inset-0 z-[150] flex items-end sm:items-center justify-center p-0 sm:p-4 animate-in fade-in duration-200">
            {/* Backdrop */}
            <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-md" onClick={onClose}></div>

            {/* Dialog Content */}
            <div className="relative bg-white/95 backdrop-blur-xl w-full max-w-2xl rounded-t-[2.5rem] sm:rounded-3xl shadow-2xl h-[85vh] sm:h-auto sm:max-h-[85vh] flex flex-col overflow-hidden animate-in slide-in-from-bottom duration-300 border border-slate-200/50">
                {/* Header */}
                <div className="p-6 border-b border-slate-200 flex justify-between items-center bg-white sticky top-0 z-10">
                    <div>
                        <h3 className="text-xl font-black text-slate-800 leading-tight tracking-tight">
                            {title}
                        </h3>
                        <p className="text-xs text-slate-400 font-medium mt-0.5">
                            {subtitle}
                        </p>
                        <div className="mt-2 flex items-center gap-2">
                            <span className="text-[10px] font-black bg-indigo-50 text-indigo-600 px-2 py-0.5 rounded uppercase tracking-wider">
                                {item.name}
                            </span>
                            <span className="text-[10px] font-black bg-slate-100 text-slate-500 px-2 py-0.5 rounded uppercase tracking-wider">
                                Current: {tripLabel}
                            </span>
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 bg-slate-50 hover:bg-slate-100 rounded-full text-slate-400 hover:text-slate-600 transition-colors border-none"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Body / Scroll Area */}
                <div className="flex-1 overflow-y-auto p-6 space-y-4 custom-scrollbar bg-slate-50/50">
                    {purSaleEntries.map((p) => {
                        const config = localAdjustments[p.purSaleId] || { exclude: false, targetTrip: '' };
                        // Per-row candidate trips: server-curated availableTrips, minus the row's own trip.
                        const candidates = (p.availableTrips ?? []).filter(t => t.id !== p.trip);

                        // For QR mode, show the diff (originalQty - qty) in the qty badge
                        const displayQty = mode === 'quantity_reduction'
                            ? Math.max(0, Number(p.originalQty ?? p.qty) - Number(p.qty))
                            : p.qty;
                        return (
                            <div
                                key={p.purSaleId}
                                className="bg-white p-5 rounded-2xl border border-slate-100 shadow-sm flex flex-col gap-4"
                            >
                                {/* Outlet info and Qty */}
                                <div className="flex items-center justify-between border-b border-slate-100 pb-3">
                                    <div>
                                        <h4 className="font-bold text-slate-700 text-sm uppercase tracking-wide">
                                            {p.branch}
                                        </h4>
                                        <p className="text-xs text-slate-400 font-semibold mt-0.5">
                                            {mode === 'quantity_reduction' ? 'Remaining Balance' : 'Current Quantity'}
                                        </p>
                                    </div>
                                    <div className="text-right">
                                        <span className="text-2xl font-black text-indigo-600">
                                            {displayQty}
                                        </span>
                                        <span className="text-[10px] font-bold text-slate-400 uppercase ml-1">
                                            {item.unit}
                                        </span>
                                    </div>
                                </div>

                                {/* Controls */}
                                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 items-center">
                                    {/* Exclude Checkbox */}
                                    <label
                                        className={`flex items-center gap-3 p-3 rounded-xl border-2 cursor-pointer transition-all ${config.exclude
                                                ? 'bg-red-50/60 border-red-500/30 text-red-700'
                                                : 'bg-slate-50 border-transparent text-slate-600 hover:bg-slate-100'
                                            }`}
                                    >
                                        <input
                                            type="checkbox"
                                            checked={config.exclude}
                                            onChange={() => handleToggleExclude(p.purSaleId)}
                                            className="w-4 h-4 rounded border-slate-300 text-red-600 focus:ring-red-500"
                                            disabled={candidates.length === 0}
                                        />
                                        <div className="flex items-center gap-1.5 font-bold text-xs uppercase tracking-wider">
                                            <Ban className="w-3.5 h-3.5" />
                                            {mode === 'quantity_reduction' ? 'Ignore Diff' : 'Fully Exclude'}
                                        </div>
                                    </label>

                                    {/* Target Trip Dropdown — per-row candidates, excluding row's own trip */}
                                    <div className="flex flex-col">
                                        <label className="text-[9px] font-bold text-slate-400 uppercase tracking-widest mb-1.5 ml-1">
                                            Route to Target Trip
                                        </label>
                                        <select
                                            disabled={config.exclude || candidates.length === 0}
                                            value={config.targetTrip}
                                            onChange={(e) => handleChangeTargetTrip(p.purSaleId, e.target.value)}
                                            className={`w-full p-3 bg-slate-50 border-2 border-transparent rounded-xl outline-none font-semibold text-xs transition-all ${config.exclude
                                                    ? 'opacity-40 cursor-not-allowed text-slate-400'
                                                    : 'focus:border-indigo-500 text-slate-700'
                                                }`}
                                        >
                                            {candidates.map((opt, index) => (
                                                <option key={opt.id ?? `${opt.name}-${index}`} value={opt.id}>
                                                    {opt.name}
                                                </option>
                                            ))}
                                            {candidates.length === 0 && (
                                                <option value="">No subsequent trips generated</option>
                                            )}
                                        </select>
                                    </div>
                                </div>
                            </div>
                        );
                    })}

                    {purSaleEntries.length === 0 && (
                        <p className="text-center text-slate-400 py-6">No distributions loaded for adjustment.</p>
                    )}
                </div>

                {/* Footer */}
                <div className="p-6 bg-white border-t border-slate-200 sticky bottom-0 flex gap-4">
                    <button
                        onClick={onClose}
                        className="flex-1 py-4 bg-slate-100 hover:bg-slate-200 text-slate-500 font-bold rounded-2xl border-none transition-colors text-base"
                    >
                        Cancel
                    </button>
                    <button
                        onClick={handleSave}
                        className="flex-[2] py-4 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-2xl shadow-xl hover:shadow-indigo-100 transition-all text-base flex items-center justify-center gap-2 border-none"
                    >
                        Update Invoice
                    </button>
                </div>
            </div>
        </div>
    );
};

export default AdjustmentModal;
