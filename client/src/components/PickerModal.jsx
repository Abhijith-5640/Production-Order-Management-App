import React from 'react';
import { Layers, Truck, ChevronDown, CheckCircle } from 'lucide-react';

const PickerModal = ({ isOpen, onClose, type, options, activeOption, onSelect }) => {
    if (!isOpen) return null;

    const title = type === 'section' ? "Select Production Section" : "Select Delivery Trip";

    return (
        <div className="fixed inset-0 z-[100] flex items-end justify-center">
            <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={onClose}></div>
            <div className="relative bg-white w-full rounded-t-[2.5rem] p-8 animate-in slide-in-from-bottom duration-300 shadow-2xl">
                <div className="w-12 h-1.5 bg-slate-200 rounded-full mx-auto mb-6"></div>
                <h3 className="text-xl font-bold mb-6 text-slate-800">{title}</h3>

                <div className="space-y-2 overflow-y-auto max-h-[50vh] custom-scrollbar">
                    {options.map((opt) => {
                        const isActive = activeOption?.id === opt.id;
                        const display = type === 'section' ? opt.name : opt.trip;
                        return (
                            <button
                                key={opt.id}
                                onClick={() => onSelect(opt)}
                                className="w-full text-left p-5 rounded-xl border border-slate-100 hover:bg-indigo-50 font-bold flex justify-between items-center mb-1 transition-all bg-white shadow-none text-slate-700"
                            >
                                <span className={isActive ? 'text-indigo-600' : ''}>{display}</span>
                                {isActive && <CheckCircle className="w-5 h-5 text-indigo-600" />}
                            </button>
                        )
                    })}
                    {options.length === 0 && (
                        <p className="text-slate-400 text-center py-4">No options available</p>
                    )}
                </div>
            </div>
        </div>
    );
};

export default PickerModal;
