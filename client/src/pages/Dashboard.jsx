import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Factory, LogOut, Layers, Truck, ChevronDown, Check, PackageSearch, Ban, ClipboardList } from 'lucide-react';

import { api } from '../services/api';
import FullScreenLoader from '../components/FullScreenLoader';
import PickerModal from '../components/PickerModal';
import DetailModal from '../components/DetailModal';
import AdjustmentModal from '../components/AdjustmentModal';

const Dashboard = () => {
    const navigate = useNavigate();

    // State
    const [loading, setLoading] = useState({ state: false, text: '' });
    const [orderData, setOrderData] = useState([]);
    const [sections, setSections] = useState([]);
    const [trips, setTrips] = useState([]);

    // Selection State
    const [currentSection, setCurrentSection] = useState(null);
    const [currentTrip, setCurrentTrip] = useState(null);

    // Modals
    const [pickerConfig, setPickerConfig] = useState({ isOpen: false, type: 'section' }); // 'section' | 'trip'
    const [detailModal, setDetailModal] = useState({ isOpen: false, item: null });
    const [adjustmentModal, setAdjustmentModal] = useState({
        isOpen: false,
        item: null,
        mode: '', // 'full_exclude' | 'single_exclude' | 'quantity_reduction'
        branchesToAdjust: [],
        newQuantities: null
    });

    // Add initial loading logic similar to confirming action in old code
    const [showConfirm, setShowConfirm] = useState(false);

    // Computed data
    const hasSelection = currentSection && currentTrip;

    useEffect(() => {
        fetchSections();
        checkPendingOrders();
    }, []);

    const fetchSections = async () => {
        try {
            const { sections } = await api.getSections();
            setSections(sections || []);
        } catch (error) {
            toast.error('Failed to load sections. Is DB Server running?');
        }
    };

    const checkPendingOrders = async () => {
        try {
            const { pendingExist } = await api.checkPendingOrders();
            setShowConfirm(pendingExist);
        } catch (error) {
            console.error('Failed to check pending orders:', error);
        }
    };

    const handleGenerateInvoices = async () => {
        setShowConfirm(false);
        setLoading({ state: true, text: 'Generating Invoices...' });
        try {
            const userId = localStorage.getItem('nexus_user_id');
            const result = await api.generateInvoices(userId);
            if (result.success) {
                toast.success(result.message || 'Invoices generated successfully!');
                await fetchSections();
                if (currentSection) {
                    const { trips } = await api.getTrips(currentSection);
                    setTrips(trips || []);
                }
            } else {
                toast.error(result.message || 'Failed to generate invoices.');
            }
        } catch (error) {
            toast.error('Failed to generate invoices. Server error.');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handleLogout = () => {
        localStorage.removeItem('nexus_authenticated');
        navigate('/login');
    };

    const openSectionPicker = () => {
        setPickerConfig({ isOpen: true, type: 'section' });
    };

    const openTripPicker = async () => {
        if (!currentSection) return;
        setLoading({ state: true, text: 'Loading Trips...' });
        try {
            const { trips } = await api.getTrips(currentSection);
            setTrips(trips || []);
            //console.log(trips);
            setPickerConfig({ isOpen: true, type: 'trip' });
        } catch (error) {
            toast.error('Failed to load trips');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handlePickerSelect = (option) => {
        if (pickerConfig.type === 'section') {
            setCurrentSection(option);
            setCurrentTrip(null);
            setOrderData([]);
        } else {
            setCurrentTrip(option);
            loadOrders(currentSection, option);
        }
        setPickerConfig({ ...pickerConfig, isOpen: false });
    };

    const loadOrders = async (section, trip) => {
        setLoading({ state: true, text: 'Loading Order List...' });
        try {
            const { orders } = await api.getOrders(section, trip);
            //console.log(orders);
            setOrderData(orders || []);
        } catch (error) {
            toast.error('Failed to load orders');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handleOpenDetail = (item) => {
        // Deep clone the object so adjustments are isolated until save
        setDetailModal({ isOpen: true, item: JSON.parse(JSON.stringify(item)) });
    };

    const handleUpdateQty = (idx, delta) => {
        const updatedItem = { ...detailModal.item };
        updatedItem.distribution[idx].qty = Math.max(0, updatedItem.distribution[idx].qty + delta);
        setDetailModal({ ...detailModal, item: updatedItem });
    };

    const handleSaveInvoice = async () => {
        const { item } = detailModal;
        const originalItem = orderData.find(i => i.id === item.id);
        if (!originalItem) return;

        const reductions = [];
        const finalUpdates = [];

        item.distribution.forEach(dist => {
            const origDist = originalItem.distribution.find(d => d.branch === dist.branch);
            const origQty = origDist ? origDist.qty : 0;
            const newQty = dist.qty;

            if (newQty < origQty) {
                reductions.push({
                    branch: dist.branch,
                    qty: origQty - newQty,
                    currentQty: newQty
                });
            } else {
                finalUpdates.push({
                    branch: dist.branch,
                    currentQty: newQty,
                    balanceQty: 0,
                    balanceAction: 'none',
                    targetTrip: null
                });
            }
        });

        if (reductions.length > 0) {
            setAdjustmentModal({
                isOpen: true,
                item,
                mode: 'quantity_reduction',
                branchesToAdjust: reductions,
                newQuantities: item.distribution
            });
        } else {
            setLoading({ state: true, text: 'Updating Trip Invoice...' });
            try {
                const result = await api.adjustInvoice(item.id, currentTrip, currentSection, finalUpdates);
                if (result.success) {
                    toast.success(`Invoices updated for ${currentTrip}`);
                    setDetailModal({ isOpen: false, item: null });
                    await loadOrders(currentSection, currentTrip);
                } else {
                    toast.error(result.message || 'Failed to update invoice');
                }
            } catch (error) {
                toast.error('Failed to update invoice');
            } finally {
                setLoading({ state: false, text: '' });
            }
        }
    };

    const handleOpenFullExclude = (item) => {
        const branches = item.distribution.map(d => ({
            branch: d.branch,
            qty: d.qty,
            currentQty: 0
        }));
        setAdjustmentModal({
            isOpen: true,
            item,
            mode: 'full_exclude',
            branchesToAdjust: branches,
            newQuantities: null
        });
    };

    const handleOpenSingleExclude = (item, branchName) => {
        const dist = item.distribution.find(d => d.branch === branchName);
        if (!dist) return;
        setAdjustmentModal({
            isOpen: true,
            item,
            mode: 'single_exclude',
            branchesToAdjust: [{
                branch: branchName,
                qty: dist.qty,
                currentQty: 0
            }],
            newQuantities: null
        });
    };

    const handleAdjustmentConfirm = async (adjustmentUpdates) => {
        setAdjustmentModal(prev => ({ ...prev, isOpen: false }));
        setLoading({ state: true, text: 'Updating Invoices...' });
        try {
            const { item, mode, newQuantities } = adjustmentModal;
            let finalUpdates = [];

            if (mode === 'quantity_reduction') {
                const originalItem = orderData.find(i => i.id === item.id);
                if (originalItem && newQuantities) {
                    newQuantities.forEach(dist => {
                        const adj = adjustmentUpdates.find(a => a.branch === dist.branch);
                        if (adj) {
                            finalUpdates.push(adj);
                        } else {
                            finalUpdates.push({
                                branch: dist.branch,
                                currentQty: dist.qty,
                                balanceQty: 0,
                                balanceAction: 'none',
                                targetTrip: null
                            });
                        }
                    });
                }
            } else {
                finalUpdates = adjustmentUpdates;
            }

            const result = await api.adjustInvoice(item.id, currentTrip, currentSection, finalUpdates);
            if (result.success) {
                toast.success(result.message || 'Invoice updated successfully!');
                setDetailModal({ isOpen: false, item: null });
                await loadOrders(currentSection, currentTrip);
            } else {
                toast.error(result.message || 'Failed to update invoices.');
            }
        } catch (error) {
            toast.error('Server error while applying adjustments.');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    return (
        <>
            <FullScreenLoader isVisible={loading.state} text={loading.text} />

            {/* Initialize Modal */}
            {showConfirm && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center p-4">
                    <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm"></div>
                    <div className="relative bg-white w-full max-w-sm rounded-[2rem] shadow-2xl p-8 text-center animate-in zoom-in duration-150 border border-slate-100">
                        <div className="w-20 h-20 rounded-full flex items-center justify-center mx-auto mb-4 bg-indigo-50 text-indigo-600">
                            <ClipboardList className="w-10 h-10" />
                        </div>
                        <h3 className="text-xl font-bold text-slate-800 mb-2">Pending Orders</h3>
                        <p className="text-slate-500 text-sm mb-8">New branch orders detected for today. Generate invoices?</p>
                        <div className="flex gap-3">
                            <button
                                onClick={() => setShowConfirm(false)}
                                className="flex-1 py-4 bg-slate-100 text-slate-500 font-bold rounded-2xl border-none">
                                Later
                            </button>
                            <button
                                onClick={handleGenerateInvoices}
                                className="flex-1 py-4 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-2xl shadow-lg border-none transition-colors">
                                Generate
                            </button>
                        </div>
                    </div>
                </div>
            )}



            {/* Navbar */}
            <nav className="glass-header sticky top-0 z-40 px-6 py-3 flex justify-between items-center bg-white/80 backdrop-blur-xl border-b border-slate-200">
                <div className="flex items-center gap-3">
                    <div className="bg-indigo-600 p-2 rounded-xl shadow-lg">
                        <Factory className="text-white w-5 h-5" />
                    </div>
                    <div>
                        <h1 className="font-bold text-base leading-tight">Nexus Prod</h1>
                        <p className="text-[9px] text-indigo-600 font-bold uppercase tracking-widest">
                            {currentSection || 'Active Section'}
                        </p>
                    </div>
                </div>
                <button
                    onClick={handleLogout}
                    className="p-2 bg-white border border-slate-200 rounded-full text-slate-400 hover:text-red-500 transition-colors shadow-sm">
                    <LogOut className="w-4 h-4" />
                </button>
            </nav>

            <main className="max-w-4xl mx-auto p-4 lg:p-8">
                {/* Step Cards */}
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3 mb-6">
                    <button
                        onClick={openSectionPicker}
                        className="bg-white p-4 rounded-2xl shadow-sm border border-slate-200 flex items-center justify-between touch-active text-left w-full h-full min-h-[5rem]">
                        <div className="flex items-center gap-3">
                            <div className="text-purple-600"><Layers className="w-5 h-5" /></div>
                            <div>
                                <p className="text-[10px] text-slate-400 font-bold uppercase tracking-wider">Step 1</p>
                                <p className="font-bold text-slate-700 leading-tight">{currentSection || 'Select Section'}</p>
                            </div>
                        </div>
                        <ChevronDown className="text-slate-300 w-4 h-4" />
                    </button>

                    <button
                        onClick={openTripPicker}
                        disabled={!currentSection}
                        className={`bg-white p-4 rounded-2xl shadow-sm border border-slate-200 flex items-center justify-between touch-active text-left w-full h-full min-h-[5rem] transition-opacity ${!currentSection ? 'opacity-50 cursor-not-allowed' : 'opacity-100'}`}>
                        <div className="flex items-center gap-3">
                            <div className="text-emerald-600"><Truck className="w-5 h-5" /></div>
                            <div>
                                <p className="text-[10px] text-slate-400 font-bold uppercase tracking-wider">Step 2</p>
                                <p className="font-bold text-slate-700 leading-tight">{currentTrip || 'Select Trip'}</p>
                            </div>
                        </div>
                        <ChevronDown className="text-slate-300 w-4 h-4" />
                    </button>
                </div>

                {/* List / Empty State */}
                {!hasSelection || orderData.length === 0 ? (
                    <div className="bg-white rounded-[2rem] p-12 text-center border-2 border-dashed border-slate-200 mt-10">
                        <PackageSearch className="text-slate-200 w-12 h-12 mx-auto mb-4" />
                        <h3 className="text-lg font-bold text-slate-700">
                            {hasSelection ? 'No Orders Found' : 'Awaiting Input'}
                        </h3>
                        <p className="text-slate-400 text-sm max-w-xs mx-auto">
                            {hasSelection
                                ? 'There are no active orders for this trip.'
                                : 'Please select both a production section and a delivery trip to generate your load list.'}
                        </p>
                    </div>
                ) : (
                    <div className="space-y-3 animate-in fade-in duration-200">
                        <div className="flex justify-between items-center px-2">
                            <h2 className="font-black text-slate-800 tracking-tight">Orders</h2>
                            <div className="bg-indigo-50 text-indigo-700 px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-widest">
                                {currentTrip}
                            </div>
                        </div>

                        <div className="space-y-2 pb-24">
                            {orderData.map((item) => {
                                const isDone = item.isCompleted;
                                const tripTotal = item.distribution.reduce((acc, d) => acc + d.qty, 0);

                                return (
                                    <div
                                        key={item.id}
                                        onClick={() => handleOpenDetail(item)}
                                        className={`p-4 rounded-xl transition-all duration-300 flex items-center justify-between cursor-pointer touch-active ${isDone ? "bg-emerald-50 border-emerald-100 opacity-80" : "bg-white border-slate-100 shadow-sm hover:border-indigo-200"}`}>

                                        <div className="flex items-center gap-3">
                                            <div className={`w-10 h-10 rounded-lg flex items-center justify-center font-bold text-[9px] uppercase ${isDone ? 'bg-emerald-500 text-white' : 'bg-slate-50 text-slate-400'}`}>
                                                {isDone ? <Check className="w-5 h-5" /> : item.unit}
                                            </div>
                                            <div>
                                                <h4 className={`font-bold ${isDone ? 'text-emerald-900' : 'text-slate-800'}`}>
                                                    {item.name}
                                                </h4>
                                                <p className={`text-[9px] font-bold uppercase tracking-widest ${isDone ? 'text-emerald-600' : 'text-slate-400'}`}>
                                                    {isDone ? 'Trip Ready' : 'Pending Load'}
                                                </p>
                                            </div>
                                        </div>
                                        <div className="flex items-center gap-3">
                                            <div className={`text-xl font-bold ${isDone ? 'text-emerald-600' : 'text-indigo-600'}`}>
                                                {tripTotal}
                                            </div>
                                            {/* Exclude All Button */}
                                            {!isDone && (
                                                <button
                                                    onClick={(e) => {
                                                        e.stopPropagation();
                                                        handleOpenFullExclude(item);
                                                    }}
                                                    className="p-3 bg-red-50 text-red-500 rounded-xl hover:bg-red-500 hover:text-white transition-colors flex-shrink-0"
                                                    title="Exclude from all branches on this trip"
                                                >
                                                    <Ban className="w-5 h-5" />
                                                </button>
                                            )}
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                )}
            </main>

            {/* External Modals */}
            <PickerModal
                isOpen={pickerConfig.isOpen}
                onClose={() => setPickerConfig({ ...pickerConfig, isOpen: false })}
                type={pickerConfig.type}
                options={pickerConfig.type === 'section' ? sections : trips}
                activeOption={pickerConfig.type === 'section' ? currentSection : currentTrip}
                onSelect={handlePickerSelect}
            />

            <DetailModal
                                                isOpen={detailModal.isOpen}
                                                activeItem={detailModal.item}
                                                currentTrip={currentTrip}
                                                onClose={() => setDetailModal({ isOpen: false, item: null })}
                                                onUpdateQty={handleUpdateQty}
                                                onSave={handleSaveInvoice}
                                                onExcludeItem={(itemId, branch) => handleOpenSingleExclude(detailModal.item, branch)}
                                            />

                                            <AdjustmentModal
                                                isOpen={adjustmentModal.isOpen}
                                                item={adjustmentModal.item}
                                                currentTrip={currentTrip}
                                                trips={trips}
                                                onClose={() => setAdjustmentModal(prev => ({ ...prev, isOpen: false }))}
                                                onConfirm={handleAdjustmentConfirm}
                                                mode={adjustmentModal.mode}
                                                branchesToAdjust={adjustmentModal.branchesToAdjust}
                                            />
        </>
    );
};

export default Dashboard;
