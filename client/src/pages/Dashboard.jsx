import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Factory, LogOut, Layers, Truck, ChevronDown, Check, PackageSearch, Ban, ClipboardList } from 'lucide-react';

import { api } from '../services/api';
import FullScreenLoader from '../components/FullScreenLoader';
import PickerModal from '../components/PickerModal';
import DetailModal from '../components/DetailModal';

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
    const [excludeConfirm, setExcludeConfirm] = useState({ isOpen: false, itemId: null, branch: null, brnchId: null, itemName: '', branchName: '', item: null });

    // Add initial loading logic similar to confirming action in old code
    const [showConfirm, setShowConfirm] = useState(false);

    // Computed data
    const hasSelection = currentSection && currentTrip;

    useEffect(() => {
        // On first mount, only check for pending orders. Sections are loaded
        // *after* the user has decided whether to generate the day's bills —
        // either by completing the Generate flow (which calls fetchSections at
        // the end) or by choosing "Later" (see handleDismissPending).
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
            const UserBrnchId = parseInt(localStorage.getItem('nexus_user_brnch_id') ?? '0', 10) || 0;
            const { pendingExist } = await api.checkPendingOrders(UserBrnchId);
            setShowConfirm(pendingExist);
            if(!pendingExist) 
            {
                fetchSections();
            }
        } catch (error) {
            console.error('Failed to check pending orders:', error);
        }
    };

    // Called when the user dismisses the "Pending Orders" prompt with
    // "Later". Sections are now loaded here so the UI doesn't sit empty
    // while the user decides.
    const handleDismissPending = () => {
        setShowConfirm(false);
        fetchSections();
    };

    const handleGenerateInvoices = async () => {
        setShowConfirm(false);
        setLoading({ state: true, text: 'Generating Invoices...' });
        try {
            const userId = localStorage.getItem('nexus_user_id');
            const userCounterId = parseInt(localStorage.getItem('nexus_user_counter_id') ?? '0', 10) || 0;
            const brnchId = parseInt(localStorage.getItem('nexus_user_brnch_id') ?? '0', 10) || 0;
            const result = await api.generateInvoices(userId, brnchId, userCounterId);
            if (result.success) {
                toast.success(result.message || 'Invoices generated successfully!');
                await fetchSections();
                if (currentSection) {
                    const { trips } = await api.getTrips(currentSection.id);
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

    const handleLogout = async () => {
        try {
            await api.logout();
        } catch (error) {
            // Even if the server call fails, clear local session so the user
            // is not stuck on the dashboard with a stale token.
            console.warn('Logout request failed; clearing local session anyway.', error);
        }
        localStorage.removeItem('nexus_token');
        localStorage.removeItem('nexus_token_expires');
        localStorage.removeItem('nexus_authenticated');
        localStorage.removeItem('nexus_user');
        localStorage.removeItem('nexus_user_id');
        localStorage.removeItem('nexus_user_brnch_id');
        navigate('/login');
    };

    const openSectionPicker = () => {
        setPickerConfig({ isOpen: true, type: 'section' });
    };

    const openTripPicker = async () => {
        if (!currentSection) return;
        setLoading({ state: true, text: 'Loading Trips...' });
        try {
            const { trips } = await api.getTrips(currentSection.id);
            setTrips(trips || []);
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
            const { orders } = await api.getOrders(section.id, trip.id);
            console.log(orders);
            setOrderData(orders || []);
        } catch (error) {
            toast.error('Failed to load orders');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handleOpenDetail = (item) => {
        // Deep clone the object so adjustments are isolated until save
        const cloned = JSON.parse(JSON.stringify(item));
        // Snapshot originalQty + stockMastId at modal-open so the server-side
        // diff filter (Qty == OriginalQty) can skip unchanged rows.
        cloned.distribution = cloned.distribution.map(d => ({   
            ...d,
            originalQty: d.qty,
            stockMastId: cloned.stockMastId,
        }));
        setDetailModal({ isOpen: true, item: cloned });
    };

    const handleUpdateQty = (idx, delta) => {
        const updatedItem = { ...detailModal.item };
        updatedItem.distribution[idx].qty = Math.max(0, updatedItem.distribution[idx].qty + delta);
        setDetailModal({ ...detailModal, item: updatedItem });
    };

    const handleSaveInvoice = async () => {
        setLoading({ state: true, text: 'Updating Trip Invoice...' });
        try {
            const { item } = detailModal;
            const tripId = currentTrip.id;
            // Project DistributionDto ({ branch, trip, qty, purSaleId, stockMastId, originalQty })
            // -> UpdateOrderDistributionDto ({ purSaleId, stockMastId, originalQty, branch, qty })
            const newDistribution = item.distribution.map(d => ({
                purSaleId: d.purSaleId,
                stockMastId: d.stockMastId,
                originalQty: d.originalQty,
                branch: d.branch,
                qty: d.qty,
            }));
            const result = await api.updateInvoice(item.id, tripId, newDistribution);
            if (result.success) {
                toast.success(`Invoices updated for ${currentTrip.Trip}`);
                setDetailModal({ isOpen: false, item: null });
                // Reload order list to get refreshed completed states
                await loadOrders(currentSection, currentTrip);
            } else {
                toast.error(result.message);
            }
        } catch (error) {
            toast.error('Failed to update invoice');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handleExcludeItem = async (itemId, branch = null, brnchId) => {
        setExcludeConfirm({ isOpen: false, itemId: null, branch: null, brnchId: null, itemName: '', branchName: '', item: null });
        setLoading({ state: true, text: 'Excluding Item...' });
        try {
            // Find the item object from the latest state (either the open detail modal
            // or the list - the modal takes precedence as it may have edited distributions).
            const sourceItem = (detailModal.isOpen && detailModal.item && detailModal.item.id === itemId)
                ? detailModal.item
                : orderData.find(o => o.id === itemId || o.itemId === itemId);
            const stockMastId = sourceItem?.stockMastId ?? sourceItem?.StockMastId ?? null;

            // Build purSaleIds based on branch filter
            let purSaleIds = [];
            if (sourceItem && Array.isArray(sourceItem.distribution)) {
                if (branch === null || branch === undefined) {
                    // All branches: every purSaleId for this stockMastId
                    purSaleIds = sourceItem.distribution
                        .map(d => d.purSaleId)
                        .filter(id => id !== undefined && id !== null);
                } else {
                    // Single branch: match by brnchId (preferred) or by branch name
                    const match = sourceItem.distribution.find(d =>
                        (brnchId != null && d.brnchId === brnchId) ||
                        (branch && d.branch === branch)
                    );
                    if (match && match.purSaleId != null) {
                        purSaleIds = [match.purSaleId];
                    }
                }
            }

            const result = await api.excludeItem(
                currentSection.id,
                itemId,
                currentTrip.id,
                stockMastId,
                brnchId,
                purSaleIds
            );
            if (result.success) {
                toast.success(result.message);
                if (detailModal.isOpen) {
                    setDetailModal({ isOpen: false, item: null });
                }
                // Reload list to get updated distributions
                await loadOrders(currentSection, currentTrip);
            } else {
                toast.error(result.message || "Failed to exclude item");
            }
        } catch (error) {
            toast.error(error.message ?? "Failed to exclude item. Server error.");
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const confirmExclude = (item, branch = null, brnchId) => {
        setExcludeConfirm({
            isOpen: true,
            itemId: item.id,
            branch: branch,
            itemName: item.name,
            brnchId: brnchId,
            branchName: branch || 'All Branches',
            item: item,
        });
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
                                onClick={handleDismissPending}
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

            {/* Exclude Confirm Modal */}
            {excludeConfirm.isOpen && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center p-4">
                    <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm"></div>
                    <div className="relative bg-white w-full max-w-sm rounded-[2rem] shadow-2xl p-8 text-center animate-in zoom-in duration-150 border border-slate-100">
                        <div className="w-20 h-20 rounded-full flex items-center justify-center mx-auto mb-4 bg-red-50 text-red-500">
                            <Ban className="w-10 h-10" />
                        </div>
                        <h3 className="text-xl font-bold text-slate-800 mb-2">Exclude Item?</h3>
                        <p className="text-slate-500 text-sm mb-2">Are you sure you want to exclude <strong>{excludeConfirm.itemName}</strong>?</p>
                        <p className="text-slate-400 text-xs mb-8">Branch: <span className="font-bold text-slate-600">{excludeConfirm.branchName}</span></p>

                        <div className="flex gap-3">
                            <button
                                onClick={() => setExcludeConfirm({ isOpen: false, itemId: null, branch: null, brnchId: null, itemName: '', branchName: '', item: null })}
                                className="flex-1 py-4 bg-slate-100 text-slate-500 font-bold rounded-2xl border-none">
                                Cancel
                            </button>
                            <button
                                onClick={() => handleExcludeItem(excludeConfirm.itemId, excludeConfirm.branch, excludeConfirm.brnchId)}
                                className="flex-1 py-4 bg-red-500 hover:bg-red-600 text-white font-bold rounded-2xl shadow-lg shadow-red-200 border-none transition-colors">
                                Exclude
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
                            {currentSection?.name || 'Active Section'}
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
                                <p className="font-bold text-slate-700 leading-tight">{currentSection?.name || 'Select Section'}</p>
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
                                <p className="font-bold text-slate-700 leading-tight">{currentTrip?.trip || 'Select Trip'}</p>
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
                                {currentTrip.trip}
                            </div>
                        </div>

                        <div className="space-y-2 pb-24">
                            {orderData.map((item) => {
                                const isDone = item.isCompleted;
                                const tripTotal = item.totalQty;

                                return (
                                    <div
                                        key={item.itemId}
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
                                                        confirmExclude(item, null, null);
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
                onExcludeItem={(itemId, branch,brnchId) => confirmExclude(detailModal.item, branch, brnchId)}
            />
        </>
    );
};

export default Dashboard;
