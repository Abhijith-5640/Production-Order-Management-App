const mysql = require('mysql2/promise');
const fs = require('fs');
const path = require('path');

const isPackaged = typeof process.pkg !== 'undefined';
const configPath = isPackaged
    ? path.join(path.dirname(process.execPath), 'db_config.json')
    : path.join(__dirname, '../db_config.json');

// Global connection pool
let pool = null;

async function getPool() {
    if (!pool) {
        let configData;
        try {
            const rawConfig = fs.readFileSync(configPath, 'utf8');
            configData = JSON.parse(rawConfig);
        } catch (error) {
            console.error("Failed to read database configuration from:", configPath, error);
            throw new Error("Database configuration not found or invalid");
        }

        pool = mysql.createPool({
            host: configData.config.host,
            user: configData.config.user,
            password: configData.config.password,
            database: configData.config.database,
            port: configData.config.port,
            waitForConnections: true,
            connectionLimit: 10,
            queueLimit: 0
        });
    }
    return pool;
}

const toBool = (val) => val && (Buffer.isBuffer(val) ? val[0] === 1 : !!val);

async function loginUser(username, password) {
    try {
        const db = await getPool();
        const [rows] = await db.execute(
            'SELECT user_id, user_name, def_branch, CAST(is_active AS UNSIGNED) as is_active FROM user_master WHERE user_name = ? AND user_pass = ?',
            [username, password]
        );
        if (rows.length > 0 && (rows[0].is_active === 1 || toBool(rows[0].is_active))) {
            return { success: true, user: rows[0].user_name, userId: rows[0].user_id };
        }
        return { success: false, message: "Invalid credentials" };
    } catch (error) {
        console.error("DB Login Error:", error);
        return { success: false, message: "Database connection failed" };
    }
}

async function checkPendingOrders() {
    try {
        const db = await getPool();
        const [rows] = await db.execute('SELECT COUNT(*) as count FROM order_distribution WHERE inv_gen = 0');
        const count = rows[0].count;
        return { success: true, pendingExist: count > 0 };
    } catch (error) {
        console.error("DB checkPendingOrders Error:", error);
        return { success: false, pendingExist: false, message: error.message };
    }
}

async function generateInvoices(userId) {
    const db = await getPool();
    const connection = await db.getConnection();
    try {
        await connection.beginTransaction();

        // 1. Get all combinations of branch_id and trip_id with pending orders
        const [groups] = await connection.execute(
            'SELECT DISTINCT branch_id, trip_id FROM order_distribution WHERE inv_gen = 0'
        );

        if (groups.length === 0) {
            await connection.commit();
            connection.release();
            return { success: true, message: "No pending orders to generate" };
        }

        // Get max invoice number currently
        const [maxNoRows] = await connection.execute('SELECT COALESCE(MAX(invoice_no), 0) as max_no FROM sales_master');
        let currentMaxNo = maxNoRows[0].max_no;

        for (const group of groups) {
            const { branch_id, trip_id } = group;

            // Get pending items for this group
            const [items] = await connection.execute(
                `SELECT od.item_id, od.qty, i.price 
                 FROM order_distribution od 
                 JOIN items i ON od.item_id = i.item_id 
                 WHERE od.branch_id = ? AND od.trip_id = ? AND od.inv_gen = 0`,
                [branch_id, trip_id]
            );

            if (items.length === 0) continue;

            // Calculate total value
            let totalValue = 0;
            for (const item of items) {
                totalValue += Number(item.price) * item.qty;
            }

            // Create sales_master entry
            currentMaxNo++;
            const [smResult] = await connection.execute(
                `INSERT INTO sales_master (branch_id, invoice_prefix, invoice_no, invoice_date, total_value, created_user, trip_id) 
                 VALUES (?, 'INV', ?, NOW(), ?, ?, ?)`,
                [branch_id, currentMaxNo, totalValue, userId || 1, trip_id]
            );

            const salesMasterId = smResult.insertId;

            // Create sales_details entries
            for (const item of items) {
                const totalItemPrice = Number(item.price) * item.qty;
                await connection.execute(
                    `INSERT INTO sales_details (sales_master_id, item_id, price, qty, total) 
                     VALUES (?, ?, ?, ?, ?)`,
                    [salesMasterId, item.item_id, item.price, item.qty, totalItemPrice]
                );
            }

            // Mark order_distribution rows as generated
            await connection.execute(
                'UPDATE order_distribution SET inv_gen = 1 WHERE branch_id = ? AND trip_id = ? AND inv_gen = 0',
                [branch_id, trip_id]
            );
        }

        await connection.commit();
        connection.release();
        return { success: true, message: `${groups.length} invoices generated successfully.` };
    } catch (error) {
        await connection.rollback();
        connection.release();
        console.error("DB generateInvoices Error:", error);
        return { success: false, message: "Invoice generation failed: " + error.message };
    }
}

async function getSections() {
    try {
        const db = await getPool();
        const [rows] = await db.execute('SELECT seection_id, section_name FROM sections WHERE is_active = 1 ORDER BY seection_id ASC');
        return rows.map(row => ({
            id: row.seection_id,
            name: row.section_name
        }));
    } catch (error) {
        console.error("DB getSections Error:", error);
        return [];
    }
}

async function getTrips(sectionName) {
    try {
        const db = await getPool();
        // Load trips from sales data (invoices) that have items in the section
        const [rows] = await db.execute(
            `SELECT DISTINCT tm.trip_name, tm.trip_id 
             FROM sales_master sm 
             JOIN sales_details sd ON sm.sales_master_id = sd.sales_master_id 
             JOIN items i ON sd.item_id = i.item_id 
             JOIN sections s ON i.section_id = s.seection_id 
             JOIN trip_master tm ON sm.trip_id = tm.trip_id 
             WHERE s.section_name = ?
             ORDER BY tm.trip_id`,
            [sectionName]
        );
        return rows.map(row => ({
            id: row.trip_id,
            name: row.trip_name
        }));
    } catch (error) {
        console.error("DB getTrips Error:", error);
        return [];
    }
}

async function getOrders(sectionName, tripName) {
    try {
        const db = await getPool();
        // Load items and distributions from sales details for this section and trip
        const [rows] = await db.execute(
            `SELECT 
                 i.item_id AS id, 
                 i.item_name AS name, 
                 i.unit AS unit, 
                 sd.sales_detail_id, 
                 sd.qty, 
                 bm.branch_name AS branch, 
                 sd.is_completed 
             FROM items i 
             JOIN sections s ON i.section_id = s.seection_id 
             JOIN sales_details sd ON i.item_id = sd.item_id 
             JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id 
             JOIN branch_master bm ON sm.branch_id = bm.branch_id 
             JOIN trip_master tm ON sm.trip_id = tm.trip_id 
             WHERE s.section_name = ? AND tm.trip_name = ?`,
            [sectionName, tripName]
        );

        // Group rows by item id
        const itemsMap = new Map();
        rows.forEach(row => {
            const completed = toBool(row.is_completed);
            if (!itemsMap.has(row.id)) {
                itemsMap.set(row.id, {
                    id: row.id,
                    name: row.name,
                    unit: row.unit,
                    isCompleted: true, // will be updated based on branches
                    distribution: []
                });
            }

            const item = itemsMap.get(row.id);
            item.distribution.push({
                branch: row.branch,
                trip: tripName,
                qty: Number(row.qty)
            });

            if (!completed) {
                item.isCompleted = false;
            }
        });

        return Array.from(itemsMap.values()).sort((a, b) => a.isCompleted - b.isCompleted);
    } catch (error) {
        console.error("DB getOrders Error:", error);
        return [];
    }
}

async function updateInvoice(itemId, tripName, newDistribution) {
    const db = await getPool();
    const connection = await db.getConnection();
    try {
        await connection.beginTransaction();

        const affectedMasterIds = new Set();

        for (const dist of newDistribution) {
            // Find the sales_details record for this item, trip, and branch
            const [rows] = await connection.execute(
                `SELECT sd.sales_detail_id, sd.sales_master_id 
                 FROM sales_details sd 
                 JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id 
                 JOIN trip_master tm ON sm.trip_id = tm.trip_id 
                 JOIN branch_master bm ON sm.branch_id = bm.branch_id 
                 WHERE sd.item_id = ? AND tm.trip_name = ? AND bm.branch_name = ?`,
                [itemId, tripName, dist.branch]
            );

            if (rows.length > 0) {
                const detailId = rows[0].sales_detail_id;
                const masterId = rows[0].sales_master_id;
                affectedMasterIds.add(masterId);

                // Update qty, total and set is_completed = 1
                await connection.execute(
                    'UPDATE sales_details SET qty = ?, total = price * ?, is_completed = 1 WHERE sales_detail_id = ?',
                    [dist.qty, dist.qty, detailId]
                );
            }
        }

        // Recalculate total_value for affected invoices
        for (const masterId of affectedMasterIds) {
            await connection.execute(
                `UPDATE sales_master sm 
                 SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = ?) 
                 WHERE sm.sales_master_id = ?`,
                [masterId, masterId]
            );
        }

        await connection.commit();
        connection.release();
        return { success: true, message: `Invoice updated in MySQL for item ${itemId}` };
    } catch (error) {
        await connection.rollback();
        connection.release();
        console.error("DB updateInvoice Error:", error);
        return { success: false, message: "Failed to update database: " + error.message };
    }
}

async function excludeItem(sectionName, itemId, currentTripName, branchName) {
    const db = await getPool();
    const connection = await db.getConnection();
    try {
        await connection.beginTransaction();

        // 1. Get all active trips to determine next trip
        const [trips] = await connection.execute('SELECT trip_id, trip_name FROM trip_master WHERE is_active = 1 ORDER BY trip_id');
        const currentTripIndex = trips.findIndex(t => t.trip_name === currentTripName);
        const nextTrip = currentTripIndex >= 0 && currentTripIndex < trips.length - 1 ? trips[currentTripIndex + 1] : null;

        // 2. Select sales details to exclude/rollover
        // We select the ones matching the itemId, currentTripName, and optionally branchName
        let selectSql = `
            SELECT sd.sales_detail_id, sd.sales_master_id, sd.qty, sd.price, sm.branch_id 
            FROM sales_details sd 
            JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id 
            JOIN trip_master tm ON sm.trip_id = tm.trip_id 
        `;
        const params = [itemId, currentTripName];
        if (branchName) {
            selectSql += ` JOIN branch_master bm ON sm.branch_id = bm.branch_id WHERE sd.item_id = ? AND tm.trip_name = ? AND bm.branch_name = ?`;
            params.push(branchName);
        } else {
            selectSql += ` WHERE sd.item_id = ? AND tm.trip_name = ?`;
        }

        const [detailsToModify] = await connection.execute(selectSql, params);

        if (detailsToModify.length === 0) {
            await connection.rollback();
            connection.release();
            return { success: false, message: "No matching distribution found for exclusion." };
        }

        const affectedMasterIds = new Set();

        for (const detail of detailsToModify) {
            const { sales_detail_id, sales_master_id, qty, price, branch_id } = detail;
            affectedMasterIds.add(sales_master_id);

            // Delete the current sales detail record
            await connection.execute('DELETE FROM sales_details WHERE sales_detail_id = ?', [sales_detail_id]);

            if (nextTrip) {
                // Rollover to the next trip
                // Find or create sales_master for next trip and this branch
                const [nextMasterRows] = await connection.execute(
                    'SELECT sales_master_id FROM sales_master WHERE branch_id = ? AND trip_id = ?',
                    [branch_id, nextTrip.trip_id]
                );

                let nextMasterId;
                if (nextMasterRows.length > 0) {
                    nextMasterId = nextMasterRows[0].sales_master_id;
                } else {
                    const [maxNoRows] = await connection.execute('SELECT COALESCE(MAX(invoice_no), 0) as max_no FROM sales_master');
                    const nextNo = maxNoRows[0].max_no + 1;
                    const [insertMasterResult] = await connection.execute(
                        `INSERT INTO sales_master (branch_id, invoice_prefix, invoice_no, invoice_date, total_value, created_user, trip_id) 
                         VALUES (?, 'INV', ?, NOW(), 0, 1, ?)`,
                        [branch_id, nextNo, nextTrip.trip_id]
                    );
                    nextMasterId = insertMasterResult.insertId;
                }

                // Check if sales details already exists in next trip for this item
                const [nextDetailRows] = await connection.execute(
                    'SELECT sales_detail_id, qty FROM sales_details WHERE sales_master_id = ? AND item_id = ?',
                    [nextMasterId, itemId]
                );

                if (nextDetailRows.length > 0) {
                    const nextDetailId = nextDetailRows[0].sales_detail_id;
                    const newQty = Number(nextDetailRows[0].qty) + Number(qty);
                    await connection.execute(
                        'UPDATE sales_details SET qty = ?, total = price * ? WHERE sales_detail_id = ?',
                        [newQty, newQty, nextDetailId]
                    );
                } else {
                    await connection.execute(
                        'INSERT INTO sales_details (sales_master_id, item_id, price, qty, total) VALUES (?, ?, ?, ?, ?)',
                        [nextMasterId, itemId, price, qty, Number(price) * Number(qty)]
                    );
                }

                // Update the next master's total value
                await connection.execute(
                    `UPDATE sales_master sm 
                     SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = ?) 
                     WHERE sm.sales_master_id = ?`,
                    [nextMasterId, nextMasterId]
                );
            }
        }

        // Recalculate total_value for affected invoices of current trip
        for (const masterId of affectedMasterIds) {
            await connection.execute(
                `UPDATE sales_master sm 
                 SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = ?) 
                 WHERE sm.sales_master_id = ?`,
                [masterId, masterId]
            );
        }

        await connection.commit();
        connection.release();

        return {
            success: true,
            message: nextTrip
                ? `Excluded from ${currentTripName}. Rolled over to ${nextTrip.trip_name}.`
                : `Excluded from ${currentTripName}. Item removed completely as no next trip exists.`
        };
    } catch (error) {
        await connection.rollback();
        connection.release();
        console.error("DB excludeItem Error:", error);
        return { success: false, message: "Failed to exclude item: " + error.message };
    }
}

async function adjustItem(itemId, currentTripName, sectionName, updates) {
    const db = await getPool();
    const connection = await db.getConnection();
    try {
        await connection.beginTransaction();

        const affectedMasterIds = new Set();

        for (const update of updates) {
            const { branch, currentQty, balanceQty, balanceAction, targetTrip } = update;

            // 1. Get current sales details record
            const [rows] = await connection.execute(
                `SELECT sd.sales_detail_id, sd.sales_master_id, sd.price, sm.branch_id 
                 FROM sales_details sd 
                 JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id 
                 JOIN trip_master tm ON sm.trip_id = tm.trip_id 
                 JOIN branch_master bm ON sm.branch_id = bm.branch_id 
                 WHERE sd.item_id = ? AND tm.trip_name = ? AND bm.branch_name = ?`,
                [itemId, currentTripName, branch]
            );

            if (rows.length === 0) {
                // Not found on current trip
                continue;
            }

            const detailId = rows[0].sales_detail_id;
            const currentMasterId = rows[0].sales_master_id;
            const price = Number(rows[0].price);
            const branchId = rows[0].branch_id;
            affectedMasterIds.add(currentMasterId);

            // Update current trip quantity
            if (currentQty > 0) {
                await connection.execute(
                    'UPDATE sales_details SET qty = ?, total = price * ?, is_completed = 1 WHERE sales_detail_id = ?',
                    [currentQty, currentQty, detailId]
                );
            } else {
                // Excluded from current trip
                await connection.execute('DELETE FROM sales_details WHERE sales_detail_id = ?', [detailId]);
            }

            // Handle balance quantity rollover
            if (balanceQty > 0 && balanceAction === 'move' && targetTrip) {
                // Find target trip master ID
                const [targetTripRows] = await connection.execute(
                    'SELECT trip_id FROM trip_master WHERE trip_name = ?',
                    [targetTrip]
                );
                if (targetTripRows.length === 0) {
                    throw new Error(`Target trip ${targetTrip} not found`);
                }
                const targetTripId = targetTripRows[0].trip_id;

                // Find or create sales_master for target trip and branch
                const [nextMasterRows] = await connection.execute(
                    'SELECT sales_master_id FROM sales_master WHERE branch_id = ? AND trip_id = ?',
                    [branchId, targetTripId]
                );

                let targetMasterId;
                if (nextMasterRows.length > 0) {
                    targetMasterId = nextMasterRows[0].sales_master_id;
                } else {
                    const [maxNoRows] = await connection.execute('SELECT COALESCE(MAX(invoice_no), 0) as max_no FROM sales_master');
                    const nextNo = maxNoRows[0].max_no + 1;
                    const [insertMasterResult] = await connection.execute(
                        `INSERT INTO sales_master (branch_id, invoice_prefix, invoice_no, invoice_date, total_value, created_user, trip_id) 
                         VALUES (?, 'INV', ?, NOW(), 0, 1, ?)`,
                        [branchId, nextNo, targetTripId]
                    );
                    targetMasterId = insertMasterResult.insertId;
                }
                affectedMasterIds.add(targetMasterId);

                // Check if item already exists in target trip details
                const [targetDetailRows] = await connection.execute(
                    'SELECT sales_detail_id, qty FROM sales_details WHERE sales_master_id = ? AND item_id = ?',
                    [targetMasterId, itemId]
                );

                if (targetDetailRows.length > 0) {
                    const targetDetailId = targetDetailRows[0].sales_detail_id;
                    const newQty = Number(targetDetailRows[0].qty) + Number(balanceQty);
                    await connection.execute(
                        'UPDATE sales_details SET qty = ?, total = price * ? WHERE sales_detail_id = ?',
                        [newQty, newQty, targetDetailId]
                    );
                } else {
                    await connection.execute(
                        'INSERT INTO sales_details (sales_master_id, item_id, price, qty, total) VALUES (?, ?, ?, ?, ?)',
                        [targetMasterId, itemId, price, balanceQty, price * balanceQty]
                    );
                }
            }
        }

        // Recalculate total_value for all affected invoices (both current and target)
        for (const masterId of affectedMasterIds) {
            await connection.execute(
                `UPDATE sales_master sm 
                 SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = ?) 
                 WHERE sm.sales_master_id = ?`,
                [masterId, masterId]
            );
        }

        await connection.commit();
        connection.release();
        return { success: true, message: "Adjustments applied successfully." };
    } catch (error) {
        await connection.rollback();
        connection.release();
        console.error("DB adjustItem Error:", error);
        return { success: false, message: "Failed to apply adjustments: " + error.message };
    }
}

module.exports = {
    loginUser,
    checkPendingOrders,
    generateInvoices,
    getSections,
    getTrips,
    getOrders,
    updateInvoice,
    excludeItem,
    adjustItem
};
