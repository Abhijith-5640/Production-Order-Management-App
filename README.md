# Nexus Prod - Production Order Management App

## Overview
**Nexus Prod** is a full-stack Production Order Management Portal tailored for processing and tracking daily production distributions across multiple shifts (trips) and facility sections (e.g., Fresh Bakery, Beverages). It provides an intuitive interface to verify pending load sheets, confirm load completion accurately, and rapidly cycle through required branches and items.

## Technology Stack
- **Frontend/Client**: React 19, Vite, Tailwind CSS (v4), React Router DOM (v7), and Lucide-React for modern icons.
- **Backend/Server**: .NET 8 Web API (Kestrel, JWT bearer auth, Dapper + MySqlConnector for MySQL access).
- **Database**: MySQL Server (the connection settings live in `db_config.json` next to the published API exe).
- **Tooling**: Vite for client dev/build, `dotnet run` for API dev, `dotnet publish` for production builds.

## Project Structure
```text
production-order-management-app/
│
├── client/                     # React + Vite Frontend
│   ├── src/
│   │   ├── components/         # Reusable UI components (Loader, Modals)
│   │   ├── pages/              # App routes (Login, Dashboard)
│   │   ├── services/           # API fetch wrappers (api.js)
│   │   └── index.css           # Tailwind injection point
│   └── package.json
│
├── src/NexusProd.Api/          # ASP.NET Core 8 API (single-file publish)
│   ├── Api/                    #   Minimal-API endpoints + contracts
│   ├── Application/            #   Use-case handlers, abstractions
│   ├── Domain/                 #   Entities
│   ├── Infrastructure/         #   Dapper/MySQL repos, security, DI
│   ├── Updater/                #   Background self-updater service
│   └── wwwroot/                #   Built SPA (emitted by Vite)
│
├── MySQL_Assets/               # Schema + seed data (prod_app_db_meta_data.sql)
│
└── package.json                # Root npm scripts (client build/dev only)
```

## Setup & Installation

**Prerequisites:** .NET 8 SDK, Node.js (for the Vite client), and MySQL Server.

1. **Database Setup:**
   Before running the application, import the database schema and seed data into your MySQL Server. The SQL setup file is [MySQL_Assets/prod_app_db_meta_data.sql](MySQL_Assets/prod_app_db_meta_data.sql).

   Import the script via your MySQL CLI tool or database explorer (this creates the database `prod_app` and all tables/data):
   ```bash
   mysql -u root -p < MySQL_Assets/prod_app_db_meta_data.sql
   ```

2. **Install Dependencies:**
   ```bash
   npm run install:client   # installs the React/Vite client
   dotnet restore          # restores the .NET API packages
   ```

3. **Start the Application (development):**
   Run the API and the client in two terminals:
   ```bash
   # Terminal 1 — .NET API (port 5099)
   npm run dev:api

   # Terminal 2 — Vite dev server
   npm run dev:client
   ```

4. **Build for production:**
   ```bash
   npm run build:client    # builds the React SPA into src/NexusProd.Api/wwwroot
   npm run publish:api     # dotnet publish -c Release -r win-x64
   ```

## Complete Application Workflow

### 1. Database Configuration & Login
- **Configuration Panel:** On the Login Page, the user has the option to click **Database Configuration**. This opens a modal to input MySQL connection details (Host, Port, DB Name, User, Password).
- **Test & Save:** Users can test the connection in real-time. Once successful, saving writes the credentials and the `use_mock_db=false` flag directly to the API's `db_config.json` (next to the published exe).
- **Login Authentication:** After configuration, the user logs in. The server returns a short-lived JWT access token (held in JS memory) and a long-lived refresh token set as an HttpOnly cookie (`Secure` over HTTPS). The Dashboard then makes every subsequent call with the access token in the `Authorization` header; the refresh cookie is used silently to rotate the access token when it expires.

### 2. Initial Dashboard Load (Invoice Generation)
- **Pending Orders Check:** When the dashboard loads, the system checks for any new, non-billed orders for the current day.
- **Generation Prompt:** If unbilled orders exist, a prompt appears asking to **Generate Invoices**. 
- **Generate vs. Later:** 
  - Clicking **Generate** runs a backend process to compile and spawn the raw invoices for the day.
  - Clicking **Later** simply closes the prompt and loads the standard Dashboard interface.

### 3. Load Processing & Routing
- **Section Selection:** The left card prompts the user to select the production section (e.g., "Fresh Bakery"). The application then fetches available shipping trips from the generated invoices based on this section.
- **Trip Selection:** The right card unlocks, allowing the user to select a trip (e.g., "06:00 AM Trip"). *Note: If no invoices have been generated for a section, the trip selector will remain empty/locked.*
- **Order Population:** Once both Section and Trip are selected, the system pulls the specific items needed for that trip from the generated (but not yet finalized) invoices.

### 4. Item Verification & Finalization
- **Branch-Specific Quantities:** Clicking an item on the Orders list opens a pop-up detail modal. This displays the exact quantity breakdown required for each branch (store/warehouse) on that specific trip.
- **Invoice Updating:** The user can update/verify the quantities and click **Save**. This action sends a request to the backend, finalizing the invoice for those branches on this trip and marking the item as completed.
- **Item Exclusion & Trip Rollover:**
  - Users can exclude an item completely from a trip. The Dashboard order list contains a **Ban/Exclude** icon for each item, which triggers a global exclusion for all branches on that trip after passing a visual **Confirmation Modal**.
  - Users can also exclude an item for a **single branch** via the Detail Modal. This also triggers the confirmation modal to prevent accidental skips.
  - When excluded, the system automatically locates the **next available trip** for that branch and rolls the item's quantity over. If no subsequent trip exists, the quantity requirement is permanently discarded.
- **UX Optimization (Auto-Sorting):** When the save resolves, the modal closes and the item is visually marked complete (turns green). To maintain a clean, actionable UX, completed items are automatically sorted to the bottom of the list, keeping pending actions pushed to the top.

### 5. Logout
- The user can log out at any time using the logout icon in the top header, which clears the session and returns them to the initial authentication screen.

---

## API Documentation

For the complete API references, including detailed request and response payload structures, please refer to [API_DOCUMENTATION.md].

