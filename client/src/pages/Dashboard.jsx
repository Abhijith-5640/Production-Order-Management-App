import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Factory, LogOut, Layers, Truck, ChevronDown, Check, PackageSearch, Ban, ClipboardList, AlertTriangle } from 'lucide-react';

import { api, authStore, NO_SECTION_ID } from '../services/api';
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
    const [adjustmentModal, setAdjustmentModal] = useState({ isOpen: false, item: null, currentTrip: null, trips: null, mode: null, filterPurSaleId: null });
    // Discard-qty-changes confirmation. Shown when the user clicks exclude on a
    // distribution row in DetailModal whose qty has been edited via +/- but
    // not yet saved. Two buttons: Cancel (close, no change) and Discard
    // (reset qty to originalQty, then proceed to the exclude flow).
    const [discardConfirm, setDiscardConfirm] = useState({ isOpen: false, item: null, currentTrip: null, dist: null, distIdx: null });
    

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
        authStore.clearSession();
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
            console.log(orders !== null ? "Orders Loaded" : "Error Loading Orders");
            // Normalize every distribution once at load time so FE/SE/QR all
            // see the same shape (distribution singular, originalQty snapshot,
            // per-row stockMastId promoted from the parent item).
            const normalized = (orders || []).map(o => ({
                ...o,
                distribution: (o.distribution || []).map(d => ({
                    ...d,
                    originalQty: d.originalQty ?? d.qty,
                    stockMastId: d.stockMastId ?? o.stockMastId,
                })),
            }));
            setOrderData(normalized);
        } catch (error) {
            toast.error('Failed to load orders');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handleOpenDetail = (item) => {
        // Deep clone the object so adjustments are isolated until save.
        // Distribution is already normalized in loadOrders; no rename needed.
        const cloned = JSON.parse(JSON.stringify(item));
        setDetailModal({ isOpen: true, item: cloned });
    };

    const handleUpdateQty = (idx, delta) => {
        const updatedItem = { ...detailModal.item };
        updatedItem.distribution[idx].qty = Math.max(0, updatedItem.distribution[idx].qty + delta);
        setDetailModal({ ...detailModal, item: updatedItem });
    };

    // After +/- edits, if any row has qty < originalQty we must route the
    // diff via AdjustmentModal before saving. The actual save is performed
    // in `commitSaveInvoice` once the user confirms (or skips) the modal.
    const handleSaveInvoice = () => {
        const { item } = detailModal;
        if (!item) return;
        const reduced = (item.distribution || []).filter(
            d => Number(d.qty) < Number(d.originalQty ?? d.qty)
        );
        if (reduced.length > 0) {
            // Open the modal in qty_reduction mode. Don't close DetailModal —
            // if user cancels, they keep their edits and can re-click Save.
            setAdjustmentModal({
                isOpen: true,
                item,
                currentTrip,
                trips: trips || [],
                mode: 'quantity_reduction',
                filterPurSaleId: null,
            });
            return;
        }
        // No reductions — go straight to save.
        commitSaveInvoice();
    };

    const commitSaveInvoice = async () => {
        setLoading({ state: true, text: 'Updating Trip Invoice...' });
        try {
            const { item } = detailModal;
            const tripId = currentTrip.id;
            const distribution = item.distribution.map(d => ({
                purSaleId: d.purSaleId,
                stockMastId: d.stockMastId,
                originalQty: d.originalQty,
                branch: d.branch,
                qty: d.qty,
            }));
            const UsrId = localStorage.getItem('nexus_user_id');
            const result = await api.updateInvoice(item.id, tripId, distribution, UsrId);
            if (result.success) {
                toast.success(`Invoices updated for ${currentTrip.trip || currentTrip.name || ''}`);
                setDetailModal({ isOpen: false, item: null });
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

    // Called by AdjustmentModal onConfirm in qty_reduction mode. The modal
    // already filtered the entries; we merge the route/diff decisions back
    // into the full distribution array and call updateInvoice.
    const handleAdjustmentConfirm = async (updates) => {
        // Close the AdjustmentModal immediately; show the loader during the save.
        setAdjustmentModal(prev => ({ ...prev, isOpen: false }));
        const mode = adjustmentModal.mode;

        if (mode === 'full_exclude' || mode === 'single_exclude') {
            // Collect per-row entries of rows the user chose to exclude. Each
            // entry carries the qty (reset to originalQty by the modal's dirty-
            // qty guard if applicable) and an optional targetTrip picked from
            // the dropdown. When "Fully Exclude" is checked, targetTrip is
            // forced to null inside the modal so the server fully discards.
            // Forward BOTH discard and move rows. In FE/SE mode the modal emits
            // 'discard' when the row's "Fully Exclude" checkbox is ticked, and
            // 'move' when the user chose to route the row to a later trip (the
            // server's ExcludeItemAsync handles targetTrip carry-forward the same
            // way UpdateInvoiceAsync does — see MySqlOrderRepository).
            const entries = updates
                .filter(u => u.balanceAction === 'discard' || u.balanceAction === 'move')
                .map(u => ({
                    purSaleId: u.purSaleId,
                    qty: u.qty,
                    targetTrip: u.balanceAction === 'move' ? (u.targetTrip ?? null) : null,
                }))
                .filter(e => e.purSaleId != null);
            if (entries.length === 0) {
                toast.info('No branches were excluded.');
                return;
            }
            // For full_exclude: pass null brnchId; for single_exclude: pass the
            // row's brnchId so the server can scope correctly.
            const brnchId = mode === 'single_exclude'
                ? (updates[0]?.branch ? (detailModal.item?.distribution?.find(d => d.purSaleId === updates[0].purSaleId)?.brnchId ?? null) : null)
                : null;
            const item = detailModal.isOpen ? detailModal.item : adjustmentModal.item;
            await handleExcludeItem(item.id, null, brnchId, entries, localStorage.getItem('nexus_user_id'));
            return;
        }

        if (mode === 'quantity_reduction') {
            // For each update, apply the diff routing back into the full array.
            // The "ignore" action means the user's reduced qty stands as-is
            // (no further changes). The "move" action attaches the diff to a
            // target trip — the server will handle the move when we send the
            // newDistribution payload below.
            const { item } = detailModal;
            // Merge the per-row targetTrip / reducedQty from the modal back
            // onto the distribution. For now we forward as-is and let the
            // server's UpdateInvoice handler do the heavy lifting.
            const tripId = currentTrip.id;
            const targetTripByPurSale = {};
            updates.forEach(u => {
                if (u.balanceAction === 'move' && u.targetTrip) {
                    targetTripByPurSale[u.purSaleId] = u.targetTrip;
                }
            });
            const distribution = item.distribution.map(d => ({
                purSaleId: d.purSaleId,
                stockMastId: d.stockMastId,
                originalQty: d.originalQty,
                branch: d.branch,
                qty: d.qty,
                ...(targetTripByPurSale[d.purSaleId]
                    ? { targetTrip: targetTripByPurSale[d.purSaleId] }
                    : {}),
            }));
            const UsrId = localStorage.getItem('nexus_user_id');
            setLoading({ state: true, text: 'Updating Trip Invoice...' });
            try {
                const result = await api.updateInvoice(item.id, tripId, distribution, UsrId);
                if (result.success) {
                    toast.success(`Invoices updated for ${currentTrip.trip || currentTrip.name || ''}`);
                    setDetailModal({ isOpen: false, item: null });
                    await loadOrders(currentSection, currentTrip);
                } else {
                    toast.error(result.message);
                }
            } catch (error) {
                toast.error('Failed to update invoice');
            } finally {
                setLoading({ state: false, text: '' });
            }
        }
    };

    const handleExcludeItem = async (itemId, branch = null, brnchId = null, entriesOverride = null) => {
        setLoading({ state: true, text: 'Excluding Item...' });
        try {
            // Find the item object from the latest state (either the open detail modal
            // or the list - the modal takes precedence as it may have edited distributions).
            const sourceItem = (detailModal.isOpen && detailModal.item && detailModal.item.id === itemId)
                ? detailModal.item
                : adjustmentModal.isOpen && adjustmentModal.item && adjustmentModal.item.id === itemId
                    ? adjustmentModal.item
                    : orderData.find(o => o.id === itemId || o.itemId === itemId);
            const stockMastId = sourceItem?.stockMastId ?? sourceItem?.StockMastId ?? null;

            // Build entries based on branch filter. Each entry carries purSaleId,
            // qty and an optional targetTrip that the user selected in the modal.
            let entries = [];
            if (Array.isArray(entriesOverride) && entriesOverride.length > 0) {
                entries = entriesOverride.filter(e => e && e.purSaleId != null);
            } else if (sourceItem && Array.isArray(sourceItem.distribution)) {
                if (branch === null || branch === undefined) {
                    // All branches: every purSaleId for this stockMastId
                    entries = sourceItem.distribution
                        .filter(d => d.purSaleId != null)
                        .map(d => ({
                            purSaleId: d.purSaleId,
                            qty: d.originalQty ?? d.qty,
                            targetTrip: null,
                        }));
                } else {
                    // Single branch: match by brnchId (preferred) or by branch name
                    const match = sourceItem.distribution.find(d =>
                        (brnchId != null && d.brnchId === brnchId) ||
                        (branch && d.branch === branch)
                    );
                    if (match && match.purSaleId != null) {
                        entries = [{
                            purSaleId: match.purSaleId,
                            qty: match.originalQty ?? match.qty,
                            targetTrip: null,
                        }];
                    }
                }
            }

            if (entries.length === 0) {
                toast.error('No bill entries to exclude.');
                setLoading({ state: false, text: '' });
                return;
            }
            const UsrId = localStorage.getItem('nexus_user_id');
            const result = await api.excludeItem(
                currentSection.id,
                itemId,
                currentTrip.id,
                stockMastId,
                brnchId,
                entries,
                UsrId
            );
            if (result.success) {
                toast.success(result.message);
                if (detailModal.isOpen) {
                    setDetailModal({ isOpen: false, item: null });
                }
                if (adjustmentModal.isOpen) {
                    setAdjustmentModal(prev => ({ ...prev, isOpen: false }));
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


    // Opens the AdjustmentModal for an exclude action.
    //   mode === "FE"  -> full exclude (all distributions of the item)
    //   mode === "SE"  -> single exclude (one distribution matched by purSaleId)
    // The modal needs the *current* trips list (so it can show the "Route to
    // Target Trip" dropdown). We use the trips we already loaded for this
    // section; if they're not loaded yet, we fetch them first.
    const handleExcludePop = async (item, currentTrip, mode, purSaleId) => {
        if (!item || !currentTrip || !mode) return;

        let tripsForModal = trips;
        if (!Array.isArray(tripsForModal) || tripsForModal.length === 0) {
            if (!currentSection) {
                toast.error('No section selected.');
                return;
            }
            try {
                const fetched = await api.getTrips(currentSection.id);
                tripsForModal = fetched.trips || [];
                setTrips(tripsForModal);
            } catch (error) {
                toast.error('Failed to load trips');
                return;
            }
        }

        if (mode === 'FE') {
            setAdjustmentModal({
                isOpen: true,
                item,
                currentTrip,
                trips: tripsForModal,
                mode: 'full_exclude',
                filterPurSaleId: null,
            });
        } else if (mode === 'SE') {
            // Use the modal's built-in filtering via filterPurSaleId — no need
            // to find the entry here, the modal will pick the matching row.
            setAdjustmentModal({
                isOpen: true,
                item,
                currentTrip,
                trips: tripsForModal,
                mode: 'single_exclude',
                filterPurSaleId: purSaleId ?? null,
            });
        }
    };

    // Reset the dirty row's qty to originalQty inside the open DetailModal,
    // close the discard confirm, and proceed to handleExcludePop with the
    // restored item so the user can complete the exclude as if the qty
    // had never been edited.
    const handleDiscardChanges = () => {
        const { item, distIdx, dist, currentTrip } = discardConfirm;
        if (!item || distIdx == null || distIdx < 0 || !dist) {
            setDiscardConfirm({ isOpen: false, item: null, currentTrip: null, dist: null, distIdx: null });
            return;
        }
        const updatedItem = { ...item };
        updatedItem.distribution = (updatedItem.distribution || []).map((d, i) =>
            i === distIdx ? { ...d, qty: Number(d.originalQty ?? d.qty) } : d
        );
        setDetailModal(prev => ({ ...prev, item: updatedItem }));
        const purSaleId = dist.purSaleId;
        setDiscardConfirm({ isOpen: false, item: null, currentTrip: null, dist: null, distIdx: null });
        if (purSaleId != null) {
            handleExcludePop(updatedItem, currentTrip, "SE", purSaleId);
        }
    };

    // Just close the discard confirm — leave the dirty qty as the user
    // typed it; do not open AdjustmentModal.
    const handleCancelDiscard = () => {
        setDiscardConfirm({ isOpen: false, item: null, currentTrip: null, dist: null, distIdx: null });
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

            {/* Discard qty changes confirmation — shown when the user clicks
                exclude on a row in DetailModal whose qty has been edited via
                +/- but not yet saved. */}
            {discardConfirm.isOpen && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center p-4">
                    <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm"></div>
                    <div className="relative bg-white w-full max-w-sm rounded-[2rem] shadow-2xl p-8 text-center animate-in zoom-in duration-150 border border-slate-100">
                        <div className="w-20 h-20 rounded-full flex items-center justify-center mx-auto mb-4 bg-amber-50 text-amber-500">
                            <AlertTriangle className="w-10 h-10" />
                        </div>
                        <h3 className="text-xl font-bold text-slate-800 mb-2">Quantity Mismatch Found</h3>
                        <p className="text-slate-500 text-sm mb-2">
                            The quantity for <strong>{discardConfirm.dist?.branch}</strong> has been edited.
                        </p>
                        <p className="text-slate-400 text-xs mb-8">
                            Current: <span className="font-bold text-slate-600">{discardConfirm.dist?.qty}</span>
                            <span className="mx-1">·</span>
                            Original: <span className="font-bold text-slate-600">{discardConfirm.dist?.originalQty}</span>
                        </p>
                        <p className="text-slate-500 text-xs mb-8 -mt-4">
                            To proceed with exclude, either save the invoice first or discard the quantity changes.
                        </p>
                        <div className="flex gap-3">
                            <button
                                onClick={handleCancelDiscard}
                                className="flex-1 py-4 bg-slate-100 text-slate-500 font-bold rounded-2xl border-none">
                                Cancel
                            </button>
                            <button
                                onClick={handleDiscardChanges}
                                className="flex-1 py-4 bg-indigo-600 hover:bg-indigo-700  text-white font-bold rounded-2xl shadow-xl hover:shadow-indigo-100 border-none transition-all flex items-center justify-center gap-2">
                                Discard
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
                        <p className="text-[9px] text-indigo-600 font-bold uppercase tracking-widest flex items-center gap-1">
                            {currentSection?.id === NO_SECTION_ID && (
                                <AlertTriangle className="w-3 h-3 text-amber-500" />
                            )}
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
                        className={`bg-white p-4 rounded-2xl shadow-sm border flex items-center justify-between touch-active text-left w-full h-full min-h-[5rem] transition-all ${
                            currentSection?.id === NO_SECTION_ID
                                ? 'border-amber-300 bg-amber-50'
                                : 'border-slate-200'
                        }`}>
                        <div className="flex items-center gap-3">
                            <div className={`${currentSection?.id === NO_SECTION_ID ? 'text-amber-500' : 'text-purple-600'}`}>
                                {currentSection?.id === NO_SECTION_ID ? (
                                    <AlertTriangle className="w-5 h-5" />
                                ) : (
                                    <Layers className="w-5 h-5" />
                                )}
                            </div>
                            <div>
                                <p className="text-[10px] text-slate-400 font-bold uppercase tracking-wider">Step 1</p>
                                <p className={`font-bold leading-tight ${currentSection?.id === NO_SECTION_ID ? 'text-amber-700' : 'text-slate-700'}`}>
                                    {currentSection?.name || 'Select Section'}
                                </p>
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
                                                        handleExcludePop(item, currentTrip, "FE", null);
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
                currentSection={currentSection}
                currentTrip={currentTrip}
                onClose={() => setDetailModal({ isOpen: false, item: null })}
                onUpdateQty={handleUpdateQty}
                onSave={handleSaveInvoice}
                onExcludeItem={(item, currentTrip, mode, dist) => {
                    // Find the row's index in the (possibly edited) distribution
                    // array — the `dist` argument is the row as of the time the
                    // row was rendered; the latest edits live on item.distribution.
                    const distIdx = (item.distribution || []).findIndex(d => d.purSaleId === dist.purSaleId);
                    const live = distIdx >= 0 ? item.distribution[distIdx] : dist;
                    if (Number(live.qty) !== Number(live.originalQty ?? live.qty)) {
                        // Dirty qty: ask the user to discard or cancel before
                        // opening the heavier AdjustmentModal.
                        setDiscardConfirm({
                            isOpen: true,
                            item,
                            currentTrip,
                            dist: live,
                            distIdx,
                        });
                        return;
                    }
                    handleExcludePop(item, currentTrip, "SE", dist.purSaleId);
                }}
            />

            <AdjustmentModal
                isOpen={adjustmentModal.isOpen}
                item={adjustmentModal.item}
                currentTrip={adjustmentModal.currentTrip}
                mode={adjustmentModal.mode}
                filterPurSaleId={adjustmentModal.filterPurSaleId}
                onClose={() => setAdjustmentModal(prev => ({ ...prev, isOpen: false }))}
                onConfirm={handleAdjustmentConfirm}
            />
            
        </>
    );
};

export default Dashboard;
