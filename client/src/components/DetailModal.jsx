import React from 'react';
import { X, CheckCircle, Ban, Plus, Minus } from 'lucide-react';

const DetailModal = ({ isOpen, activeItem, currentTrip, onClose, onUpdateQty, onSave, onExcludeItem }) => {
    if (!isOpen || !activeItem) return null;

    const filteredDist = activeItem.distribution || [];
    // const grandTotal = filteredDist.reduce((sum, dist) => sum + dist.qty, 0);
    const grandTotal = activeItem.TotalQty;

    return (
        <div className="fixed inset-0 z-[100] flex items-end sm:items-center justify-center p-0 sm:p-4">
            <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-md" onClick={onClose}></div>
            <div className="relative bg-white w-full max-w-2xl rounded-t-[2.5rem] sm:rounded-3xl shadow-2xl h-[92vh] sm:h-auto sm:max-h-[85vh] flex flex-col overflow-hidden animate-in slide-in-from-bottom duration-300">

                <div className="p-5 border-b border-slate-300 flex justify-between items-center bg-white sticky top-0 z-10">
                    <div>

                        <h3 className="text-xl font-black text-slate-800 leading-none tracking-tight">
                            {activeItem.name}
                        </h3>
                        <p className="text-[10px] font-black bg-indigo-50 text-indigo-600 px-2 py-0.5 rounded uppercase tracking-tighter mb-1 inline-block">
                            {activeItem.unit}
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        className="p-2 bg-slate-50 rounded-full text-slate-400 active:scale-90 transition-all border-none"
                    >
                        <X className="w-6 h-6" />
                    </button>
                </div>

                <div className="flex-1 overflow-y-auto p-4 space-y-2 custom-scrollbar bg-slate-50">
                    {filteredDist.map((dist, idx) => (
                        <div key={idx} className="bg-white p-4 rounded-2xl border border-slate-100 flex items-center justify-between shadow-sm">
                            <span className="text-xs font-bold text-slate-500 uppercase flex-1 pr-2">{dist.BranchName}</span>
                            <div className="flex items-center gap-2 bg-slate-50 p-1 rounded-xl  mr-3">
                                <button
                                    onClick={() => onUpdateQty(idx, -1)}
                                    className="w-10 h-10 rounded-lg bg-white shadow-sm flex items-center justify-center text-red-600 font-bold active:bg-slate-100 transition-all border-none"
                                >
                                    <Minus className="w-4 h-4" />
                                </button>
                                <span className="w-8 text-center font-bold text-slate-800 text-lg">{dist.Qty}</span>
                                <button
                                    onClick={() => onUpdateQty(idx, 1)}
                                    className="w-10 h-10 rounded-lg bg-white shadow-sm flex items-center justify-center text-emerald-600 font-bold active:bg-slate-100 transition-all border-none"
                                >
                                    <Plus className="w-4 h-4" />
                                </button>
                            </div>
                            <button
                                onClick={() => onExcludeItem(activeItem.StockMastId, dist.branch)}
                                className="w-10 h-10 rounded-xl bg-red-50 text-red-500 hover:bg-red-500 hover:text-white flex items-center justify-center shadow-sm active:scale-95 transition-all border-none"
                                title="Exclude this branch from the trip"
                            >
                                <Ban className="w-4 h-4" />
                            </button>
                        </div>
                    ))}
                    {filteredDist.length === 0 && (
                        <p className="text-center text-slate-400 mt-4">No distributions available for this trip.</p>
                    )}
                </div>

                <div className="p-6 bg-white border-t border-slate-300 sticky bottom-0">
                    <div className="flex justify-between items-center mb-4">
                        <p className="font-bold text-slate-400 uppercase text-[10px]">Consolidated Qty</p>
                        <p className="text-3xl font-bold text-indigo-600">{grandTotal}</p>
                    </div>
                    <button
                        onClick={onSave}
                        className="w-full py-4 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-2xl shadow-xl active:scale-95 transition-all text-lg flex items-center justify-center gap-3 border-none"
                    >
                        <CheckCircle className="w-5 h-5" /> Update Invoice
                    </button>
                </div>
            </div>
        </div>
    );
};

export default DetailModal;
