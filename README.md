# PawStock – Pet Grooming Inventory (Aparri, Cagayan)

A desktop inventory management and monitoring system for a pet grooming shop. It tracks products, stock levels, restocking needs, suppliers, expiration dates and an audit history. It also includes backup and restore.

**Stack:** VB.NET · WPF on .NET 10 · MySQL / MariaDB via XAMPP · [MySqlConnector](https://mysqlconnector.net/) · [PDFsharp-MigraDoc](https://docs.pdfsharp.net/) 6.2.4 (PDF reports) · [ClosedXML](https://github.com/ClosedXML/ClosedXML) (Excel reports)

> PDFsharp-MigraDoc is pinned to **6.2.4**, the last MIT-licensed release. Version 6.5.0 and later are AGPL-3.0, so don't upgrade it without checking the license.

---

## 1. Requirements

| What | Notes |
|---|---|
| Windows 10/11 | |
| .NET 10 SDK | Install it with Visual Studio 2026's **".NET desktop development"** workload, or `winget install Microsoft.DotNet.SDK.10` |
| XAMPP | Only the **MySQL** module is needed. Default settings: `localhost:3306`, user `root`, no password |

## 2. Run it

1. Open the **XAMPP Control Panel** and click **Start** beside **MySQL**.
2. Start the app using one of these:
   - **Visual Studio:** open `PetGroomingInventory.sln` and press **F5**.
   - **Command line:** run this from this folder:
     ```
     dotnet run --project src/PetGroomingInventory
     ```
3. On first launch the app creates the `pet_grooming_inventory` database, its tables and the default categories and settings. You don't need to import anything.
4. *(Optional)* Go to **Settings → Sample Data → Load Sample Data** to explore with demonstration records. Sample records are tagged **SAMPLE**, and **Remove Sample Data** deletes them in one step.

If MySQL isn't running, the app shows a **Can't connect to the database** screen with steps to fix it and a **Retry** button.

### Database connection settings
The connection settings are in `dbconfig.json`, next to `PawStock.exe` (in `bin/…` when running from source):

```json
{ "Server": "localhost", "Port": 3306, "User": "root", "Password": "", "Database": "pet_grooming_inventory" }
```

`database/schema.sql` holds the same schema the app runs. You can also import it with phpMyAdmin if you prefer.

## 3. Features

| Module | What it does |
|---|---|
| **Dashboard** | Six cards: Total Items, Available Stock, Low Stock, Critical Stock, Out of Stock and Expiring Soon. Each card opens a filtered list. The **Stock Alerts** table has exactly 3 columns (Item Name, Available Stock, Status). The **Recent Activities** table has exactly 2 columns (Name, Activity). There is also a Stock Health ring, an Expiration Watch list and quick actions. |
| **Inventory** | Search, and filter by category, supplier, stock status and expiration status. You can add, edit and remove items; removing asks for confirmation first. An empty state appears when there are no items. |
| **Add / Edit Item** | Fields: Item Name\*, Category\*, Pet Type\*, Quantity\* (≥ 0), Retail Price\* (≥ 0), Supplier\*, an optional Description and an optional Expiration Date. Description is an editable dropdown for size, flavor, color or variant (e.g. `500 ml`, `15 g Pink`, `Lamb`): pick a suggestion or type anything, and what you type is suggested next time. Use **+ New supplier** to add a supplier without leaving the form. When you edit an item, its quantity is read-only: change stock with Stock In or Stock Adjustment, so every change is recorded. |
| **Stock Monitoring** | Tabs: Needs Restocking (each item has a **Restock** button), Expiration Alerts, Stock-In History and Adjustment History. |
| **Stock In** | Fields: item, quantity received, date received (cannot be in the future), supplier if applicable, and an optional note. |
| **Stock Adjustment** | Add stock, deduct stock, or set a counted quantity. A reason is required, and "Other" also requires a note. A before → after preview appears, and you confirm before it's saved. Stock can never go below 0. |
| **Suppliers** | Fields: Name, Contact Person, Phone (Philippine mobile or landline), Address, and an optional Email. You can search, add, edit and delete suppliers. Deleting is blocked while items still use the supplier. Renaming a supplier updates it everywhere. |
| **Activities** | Full audit history with date and time. Filter by type, date range and text. |
| **Reports** | Choose a report type, **Inventory From** / **Inventory To** dates (or a quick range: Today, This Month, Last Month, This Year, All Time), and optionally a category and pet type, then click **Generate Report**. Reports: Inventory Summary, Inventory List, Low-Stock, Out-of-Stock, Expiring Items, Category, Pet Type and Inventory Movement. The report on screen can be printed (Print Report) or saved as PDF or Excel (.xlsx). |
| **Settings** | Stock thresholds, expiring-soon days, categories, application name, backup and restore, and sample data. |

### Admin accounts and sign-in

Every page requires an Admin to be signed in. There is only one role, **Admin**, and the system sets it; it can't be chosen or changed. No email is used anywhere.

| Screen | How it works |
|---|---|
| **Register** | Shown automatically the first time the app runs, while no Admin exists. Fields: Full Name, Username, Password, Confirm Password, Security Question (from a fixed list) and Security Answer. After the first account exists, registration is closed. |
| **Login** | Username + Password. **Remember me** remembers only the username on this computer (`%LOCALAPPDATA%\PawStock\login.json`); the password is always required. A failed sign-in always says "Invalid username or password." |
| **Forgot Password?** | Username → answer your security question → new password. The new password must differ from the previous one. Failures always say "The information provided could not be verified." A username that doesn't exist still gets a question, so the screen never reveals which usernames exist. |
| **My Profile** | Shows Full Name, Username, Role and last login. Lets you edit Full Name, Username, Security Question and Answer; your current password is required to save. The current answer is never shown. |
| **Change Password** | Current Password, New Password and Confirm New Password. Opened from My Profile. |
| **Logout** | In the sidebar. It asks "Are you sure you want to log out?", then ends the session and closes every open page. |

**Password rules:** 8–128 characters, with an uppercase letter, a lowercase letter, a number and a symbol, and the password must not contain the username. A strength meter and checklist update as you type.

**Security:**
- Passwords and security answers are stored only as salted PBKDF2-SHA256 hashes (600,000 iterations), in the `admin_accounts` table.
- After 5 failed sign-in or recovery attempts for a username within 15 minutes, that username is locked for 15 minutes. The same applies to usernames that don't exist.
- Admin accounts are **not** included in backups, so a restore never removes or rolls back an account.

**Locked out completely?** If an Admin forgets both the password and the security answer, no one can reset it from inside the app. As a last resort, delete that person's row from the `admin_accounts` table in phpMyAdmin. If no Admin is left, the app opens Register again on the next start.

### Stock status rules (configurable in Settings)

| Color | Status | Rule (defaults) |
|---|---|---|
| 🟢 Green | In Stock | quantity > low threshold (10) |
| 🟡 Yellow | Low Stock | critical threshold < quantity ≤ low threshold (6–10) |
| 🔴 Red | Critical Stock | 0 < quantity ≤ critical threshold (1–5) |
| ⚪ Gray | Out of Stock | quantity = 0 |

The app requires `0 < critical < low`, so the ranges never overlap. These four colors are used only for stock status.

**Expiration:** An item with a blank expiration date never raises an alert. An item becomes **Expiring Soon** when it expires within N days (default 30), counting today. It becomes **Expired** once the date has passed.

### Report data rules

Reports only use data the app stores, and each report states which date its range is compared with:

| Report | Date range is compared with |
|---|---|
| Inventory Summary | Stock figures are the **current** inventory. The range applies to the "Period activity" figures: items added (date added) and stock movements (transaction date). |
| Inventory List | The date each item was added |
| Expiring Items | The expiration date. Expired items and items expiring soon (Settings → expiring-soon days) are labeled separately. |
| Inventory Movement | The stock transaction date (the date received for Stock In) |
| Low-Stock, Out-of-Stock, Category, Pet Type | Not used. These show current stock, because the app does not keep past stock levels. |

- **Minimum stock level** is the Low Stock threshold in Settings. There is no separate minimum for each item.
- **Inventory value** is the retail value (quantity × retail price). The app does not store cost prices.
- **Pet type filter:** Dog and Cat also include items marked Dog & Cat, the same as the Inventory page. In the Pet Type report each item is counted once, under its own pet type, so the totals have no double counting.
- **Removed items:** their stock movements keep the item's name. When a category or pet type filter is set, those movements are left out, because the removed item's category and pet type are no longer known. The report says when this happens.

### Backup and restore
- **Create Backup** saves every table to a single `.json` file. Each backup carries a SHA-256 checksum. The default folder is `Documents\PawStock Backups`.
- **Restore** checks the file before changing anything. It checks that the file comes from this app, the format version, the checksum, the required fields, that there are no negative quantities, and that every record it refers to exists in the file.
- Before replacing anything, Restore warns you and shows what the backup contains. It then saves a **safety backup** of your current data automatically.
- The data is then replaced in one transaction. If any step fails, nothing changes.

## 4. Project structure

```
database/schema.sql              Database schema (embedded in the app and run on startup)
src/PetGroomingInventory/
  Application.xaml, MainWindow   App shell: sidebar, header, page host, toasts, database error screen
  Themes/                        Colors.xaml (palette, status colors) and Controls.xaml (control styles)
  Models/                        Item, Supplier, records and enums
  Common/                        StatusCalculator (status rules), InputValidator, Fmt, exceptions
  Data/                          Db helper, DatabaseInitializer, repositories (parameterized SQL)
  Services/                      Business rules, transactions, activity logging, backup, sample data
  Controls/                      PageBase, RingChart, converters, FormErrors, PasswordField (show/hide + strength meter)
  Views/                         Pages and dialog windows; Views/Auth holds Login, Register and Forgot Password
tests/PetGroomingInventory.Tests xUnit tests
```

How the pieces fit together:
- Every write goes through a **service**. The service validates the input, runs a single database transaction, writes the activity log entry, and then raises `AppEvents.DataChanged`.
- All pages and the header alert badge listen for `DataChanged` and reload. That keeps the dashboard cards, alerts, tables and recent activities in step with each other.

## 5. Tests

```
dotnet test
```

These tests cover the status rules, expiration rules, input validation, adjustment rules, backup-file validation, every report type, and the PDF, Excel and print outputs. They don't need a database.

To also run the database integration and UI smoke tests, start MySQL in XAMPP and run:

```
set PAWSTOCK_TEST_DB=1
dotnet test
```

These tests create throwaway databases named `pawstock_integration_test`, `pawstock_ui_smoke_test` and `pawstock_auth_test`, and drop them when they finish. They never touch `pet_grooming_inventory`.

## 6. Troubleshooting

| Problem | Fix |
|---|---|
| "Can't connect to the database" | Start MySQL in XAMPP, then click **Retry**. |
| MySQL won't start in XAMPP | Another program is using port 3306. Stop it, or change the port in XAMPP and in `dbconfig.json`. |
| "Access denied" | Update `User` and `Password` in `dbconfig.json` to match your MySQL account. |
| The database was deleted while the app was open | Click **Retry** on the connection screen; the tables are created again. |
