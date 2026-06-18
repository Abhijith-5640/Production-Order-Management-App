import React, { useState, useEffect } from 'react';
import { X, AlertCircle, Ban, ArrowRight } from 'lucide-react';

const AdjustmentModal = ({
    isOpen,
    item,
    currentTrip,
    trips = [],
    onClose,
    onConfirm,
    mode, // 'full_exclude' | 'single_exclude' | 'quantity_reduction'
    branchesToAdjust = [] // Array of { branch, qty, currentQty }
}) => {
    const [localAdjustments, setLocalAdjustments] = useState({});

    // Filter trips to only include those that are chronologically AFTER the current trip
    const currentTripIndex = trips.findIndex(
        trip => (typeof trip === 'object' ? trip.name : trip) === currentTrip
    );

    const targetTrips = (
        currentTripIndex >= 0
            ? trips.slice(currentTripIndex + 1)
            : []
    ).map(t => typeof t === 'object' ? t.name : t);

    //console.log("AdjustmentModal", trips, currentTrip);

    useEffect(() => {
        if (isOpen && branchesToAdjust.length > 0) {
            const initial = {};
            const defaultTarget = targetTrips.length > 0 ? targetTrips[0] : '';
            branchesToAdjust.forEach(b => {
                // If there are no other generated trips, user must fully exclude
                const mustExclude = targetTrips.length === 0;
                initial[b.branch] = {
                    exclude: mustExclude,
                    targetTrip: defaultTarget
                };
            });
            setLocalAdjustments(initial);
        }
    }, [isOpen, branchesToAdjust, currentTrip, trips]);

    if (!isOpen || !item) return null;

    const handleToggleExclude = (branchName) => {
        if (targetTrips.length === 0) return; // Must remain excluded if no other trips
        setLocalAdjustments(prev => ({
            ...prev,
            [branchName]: {
                ...prev[branchName],
                exclude: !prev[branchName]?.exclude
            }
        }));
    };

    const handleChangeTargetTrip = (branchName, tripName) => {
        setLocalAdjustments(prev => ({
            ...prev,
            [branchName]: {
                ...prev[branchName],
                targetTrip: tripName
            }
        }));
    };

    const handleSave = () => {
        const updates = branchesToAdjust.map(b => {
            const config = localAdjustments[b.branch] || {};
            return {
                branch: b.branch,
                currentQty: b.currentQty,
                balanceQty: b.qty,
                balanceAction: config.exclude ? 'discard' : 'move',
                targetTrip: config.exclude ? null : config.targetTrip
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
    }

    const normalizedOptions = targetTrips.map((opt, index) => {
        if (typeof opt === 'object' && opt !== null) {
            return {
                id: opt.id !== undefined ? opt.id : index,
                name: opt.name || ''
            };
        }
        return {
            id: index,
            name: String(opt)
        };
    });

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
                                Current: {currentTrip}
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
                    {branchesToAdjust.map((b) => {
                        const config = localAdjustments[b.branch] || { exclude: false, targetTrip: '' };
                        return (
                            <div
                                key={b.branch}
                                className="bg-white p-5 rounded-2xl border border-slate-100 shadow-sm flex flex-col gap-4"
                            >
                                {/* Outlet info and Qty */}
                                <div className="flex items-center justify-between border-b border-slate-100 pb-3">
                                    <div>
                                        <h4 className="font-bold text-slate-700 text-sm uppercase tracking-wide">
                                            {b.branch}
                                        </h4>
                                        <p className="text-xs text-slate-400 font-semibold mt-0.5">
                                            {mode === 'quantity_reduction' ? 'Remaining Balance' : 'Current Quantity'}
                                        </p>
                                    </div>
                                    <div className="text-right">
                                        <span className="text-2xl font-black text-indigo-600">
                                            {b.qty}
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
                                            onChange={() => handleToggleExclude(b.branch)}
                                            className="w-4 h-4 rounded border-slate-300 text-red-600 focus:ring-red-500"
                                            disabled={targetTrips.length === 0}
                                        />
                                        <div className="flex items-center gap-1.5 font-bold text-xs uppercase tracking-wider">
                                            <Ban className="w-3.5 h-3.5" />
                                            Fully Exclude
                                        </div>
                                    </label>

                                    {/* Target Trip Dropdown */}
                                    <div className="flex flex-col">
                                        <label className="text-[9px] font-bold text-slate-400 uppercase tracking-widest mb-1.5 ml-1">
                                            Route to Target Trip
                                        </label>
                                        <select
                                            disabled={config.exclude || targetTrips.length === 0}
                                            value={config.targetTrip}
                                            onChange={(e) => handleChangeTargetTrip(b.branch, e.target.value)}
                                            className={`w-full p-3 bg-slate-50 border-2 border-transparent rounded-xl outline-none font-semibold text-xs transition-all ${config.exclude
                                                    ? 'opacity-40 cursor-not-allowed text-slate-400'
                                                    : 'focus:border-indigo-500 text-slate-700'
                                                }`}
                                        >
                                            {normalizedOptions
                                            .sort((a, b) => a.id - b.id)
                                            .map((opt, index) => (
                                                <option key={opt.id ?? `${opt.name}-${index}`} value={opt.name}>
                                                    {opt.name}
                                                </option>
                                            ))}
                                            {targetTrips.length === 0 && (
                                                <option value="">No subsequent trips generated</option>
                                            )}
                                        </select>
                                    </div>
                                </div>
                            </div>
                        );
                    })}

                    {branchesToAdjust.length === 0 && (
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
