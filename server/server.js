// c:/Users/devda/source/repos/anti-gra/Production-Order-Management-App/server/server.js
const express = require('express');
const cors = require('cors');
const dotenv = require('dotenv');
const fs = require('fs');
const path = require('path');
const mysql = require('mysql2/promise');

// Load env vars if any
dotenv.config();

// Determine db_config.json path and copy template if needed before DB is initialized
const isPackaged = typeof process.pkg !== 'undefined';
const configPath = isPackaged 
    ? path.join(path.dirname(process.execPath), 'db_config.json') 
    : path.join(__dirname, 'db_config.json');

if (isPackaged && !fs.existsSync(configPath)) {
    try {
        const defaultTemplatePath = path.join(__dirname, 'db_config.json');
        if (fs.existsSync(defaultTemplatePath)) {
            fs.copyFileSync(defaultTemplatePath, configPath);
            console.log('📝 Created default db_config.json at', configPath);
        } else {
            const defaultConfig = {
                config: {
                    host: "localhost",
                    port: 3306,
                    user: "root",
                    password: "admin@5555",
                    database: "prod_app"
                }
            };
            fs.writeFileSync(configPath, JSON.stringify(defaultConfig, null, 2), 'utf8');
            console.log('📝 Created default configuration file at', configPath);
        }
    } catch (err) {
        console.error('⚠️ Failed to initialize default db_config.json:', err);
    }
}

// Determine Database service (Mock vs MySQL)
const db = require('./db');

const app = express();

// Middleware
app.use(cors());
app.use(express.json());

// Routes
app.post('/api/login', async (req, res) => {
    const { username, password } = req.body;
    const result = await db.loginUser(username, password);
    if (result.success) {
        res.json(result);
    } else {
        res.status(401).json(result);
    }
});

app.get('/api/orders/check-pending', async (req, res) => {
    const result = await db.checkPendingOrders();
    res.json(result);
});

app.post('/api/orders/generate-invoices', async (req, res) => {
    const { userId } = req.body;
    const result = await db.generateInvoices(userId);
    if (result.success) {
        res.json(result);
    } else {
        res.status(500).json(result);
    }
});

app.get('/api/sections', async (req, res) => {
    const sections = await db.getSections();
    res.json({ sections });
});

app.get('/api/trips/:section', async (req, res) => {
    const { section } = req.params;
    const trips = await db.getTrips(section);
    res.json({ trips });
});

app.get('/api/orders/:section/:trip', async (req, res) => {
    const { section, trip } = req.params;
    const orders = await db.getOrders(section, trip);
    res.json({ orders });
});

app.post('/api/orders/update', async (req, res) => {
    const { itemId, trip, newDistribution } = req.body;

    if (!itemId || !trip || !newDistribution) {
        return res.status(400).json({ success: false, message: "Missing required fields" });
    }

    const result = await db.updateInvoice(itemId, trip, newDistribution);
    if (result.success) {
        res.json(result);
    } else {
        res.status(500).json(result);
    }
});

app.post('/api/orders/exclude', async (req, res) => {
    const { section, itemId, currentTrip, branch } = req.body;

    if (!section || !itemId || !currentTrip) {
        return res.status(400).json({ success: false, message: "Missing required fields for exclusion" });
    }

    // Branch can be null/undefined to exclude entirely
    const result = await db.excludeItem(section, itemId, currentTrip, branch);
    if (result.success) {
        res.json(result);
    } else {
        res.status(500).json(result);
    }
});

app.post('/api/orders/adjust', async (req, res) => {
    const { itemId, currentTrip, section, updates } = req.body;

    if (!itemId || !currentTrip || !section || !updates) {
        return res.status(400).json({ success: false, message: "Missing required fields for adjustment" });
    }

    const result = await db.adjustItem(itemId, currentTrip, section, updates);
    if (result.success) {
        res.json(result);
    } else {
        res.status(500).json(result);
    }
});

app.post('/api/config', (req, res) => {
    try {

        // Read existing structure
        let currentConfig = { config: {} };
        if (fs.existsSync(configPath)) {
            const fileData = fs.readFileSync(configPath, 'utf8');
            currentConfig = JSON.parse(fileData);
        }

        // Merge with incoming req.body 
        // req.body should have: host, port, database, user, password
        currentConfig.config = {
            host: req.body.host || currentConfig.config.host,
            port: Number(req.body.port) || currentConfig.config.port,
            user: req.body.user || currentConfig.config.user,
            password: req.body.password || currentConfig.config.password,
            database: req.body.database || currentConfig.config.database
        };

        fs.writeFileSync(configPath, JSON.stringify(currentConfig, null, 2), 'utf8');
        res.json({ success: true, message: 'Configuration saved successfully. Please restart server if needed.' });
    } catch (error) {
        console.error('Config Save Error:', error);
        res.status(500).json({ success: false, message: 'Failed to write configuration file' });
    }
});

app.post('/api/test-db', async (req, res) => {
    try {
        const { host, port, user, password, database } = req.body;

        // Create a temporary connection
        const connection = await mysql.createConnection({
            host,
            port: Number(port),
            user,
            password,
            database
        });

        await connection.ping();
        await connection.end();
        res.json({ success: true, message: 'Database connection successful!' });
    } catch (error) {
        console.error('Test DB Error:', error);
        res.json({ success: false, message: error.message || 'Database connection failed' });
    }
});

// Serve static client assets if client/dist exists
const clientDistPath = path.join(__dirname, '../client/dist');
if (fs.existsSync(clientDistPath)) {
    console.log(`📂 Serving static frontend from: ${clientDistPath}`);
    app.use(express.static(clientDistPath));
    app.get('*splat', (req, res, next) => {
        // Fall through to API 404 if it's an API route
        if (req.path.startsWith('/api')) {
            return next();
        }
        res.sendFile(path.join(clientDistPath, 'index.html'));
    });
}

const PORT = process.env.PORT || 5000;
const HOST = '0.0.0.0'; // Listen on all interfaces to allow local network devices to connect

app.listen(PORT, HOST, () => {
    console.log(`\n================================`);
    console.log(`🚀 Server running on port ${PORT}`);
    console.log(`🚀 Locally accessible at: http://localhost:${PORT}`);
    console.log(`🚀 Access from other devices: http://<your-host-ip-address>:${PORT}`);
    console.log(`================================`);
});
