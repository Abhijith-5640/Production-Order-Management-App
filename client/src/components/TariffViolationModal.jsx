import React from 'react';
import { AlertTriangle, Store } from 'lucide-react';

const TariffViolationModal = ({
    isOpen,
    violations,
    onConfirm,
    onCancel
}) => {
    if (!isOpen || !violations?.hasViolations) return null;

    return (
        <div className="fixed inset-0 z-[200] flex items-center justify-center p-4">
            <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm"></div>
            <div className="relative bg-white w-full max-w-2xl rounded-[2rem] shadow-2xl p-8 max-h-[90vh] overflow-y-auto animate-in zoom-in duration-150 border border-slate-100">
                {/* Header */}
                <div className="flex items-center gap-4 mb-6">
                    <div className="w-16 h-16 rounded-full flex items-center justify-center bg-amber-50 text-amber-500">
                        <AlertTriangle className="w-8 h-8" />
                    </div>
                    <div>
                        <h3 className="text-xl font-bold text-slate-800">Tariff Violation Detected</h3>
                        <p className="text-slate-500 text-sm">
                            {violations.totalItems} item order(s) across {violations.totalBranches} branch(es)
                            are not in the approved purchasing tariff.
                        </p>
                    </div>
                </div>

                {/* Branch-wise tables */}
                <div className="space-y-6 mb-6">
                    {violations.branches.map((branch) => (
                        <div key={branch.branchId} className="border border-slate-200 rounded-xl overflow-hidden">
                            <div className="bg-slate-50 px-4 py-3 font-bold text-slate-700 border-b border-slate-200 flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <Store className="w-4 h-4" />
                                    <span>{branch.branchName}</span>
                                </div>
                                <span className="bg-red-100 text-red-600 px-3 py-1 rounded-full text-xs font-bold">
                                    {branch.items.length} item{branch.items.length !== 1 ? 's' : ''}
                                </span>
                            </div>
                            <table className="w-full text-sm">
                                <thead>
                                    <tr className="text-slate-400 text-xs">
                                        <th className="text-left px-4 py-2 font-bold">CODE</th>
                                        <th className="text-left px-4 py-2 font-bold">ITEM NAME</th>
                                        <th className="text-left px-4 py-2 font-bold">UNIT</th>
                                        <th className="text-right px-4 py-2 font-bold">QTY</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {branch.items.map((item, idx) => (
                                        <tr key={idx} className="border-t border-slate-100">
                                            <td className="px-4 py-3">
                                                <span className="bg-slate-100 text-slate-500 px-2 py-1 rounded text-xs font-mono font-medium">
                                                    {item.itemCode}
                                                </span>
                                            </td>
                                            <td className="px-4 py-3 font-medium text-slate-700">{item.itemName}</td>
                                            <td className="px-4 py-3">
                                                <span className="bg-slate-100 text-slate-500 px-2 py-1 rounded text-xs font-medium">
                                                    {item.unit}
                                                </span>
                                            </td>
                                            <td className="px-4 py-3 text-right font-bold text-red-600">{item.qty}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    ))}
                </div>

                {/* Info Banner */}
                <div className="bg-red-100 text-red-600 rounded-xl p-4 mb-6 flex items-center justify-center gap-2">
                    <span className="font-semibold text-sm">
                        These items will be <span className="font-bold">IGNORED</span> when generating invoices and will not appear in any invoice.
                    </span>
                </div>

                {/* Actions */}
                <div className="flex gap-3">
                    <button
                        onClick={onCancel}
                        className="flex-1 py-4 bg-slate-100 text-slate-500 font-bold rounded-2xl border-none hover:bg-slate-200 transition-colors">
                        Cancel
                    </button>
                    <button
                        onClick={onConfirm}
                        className="flex-1 py-4 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-2xl shadow-lg border-none transition-colors">
                        Confirm & Proceed
                    </button>
                </div>
            </div>
        </div>
    );
};

export default TariffViolationModal;
