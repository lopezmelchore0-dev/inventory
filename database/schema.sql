-- ============================================================
-- PawStock - Pet Grooming Inventory (Aparri, Cagayan)
-- MySQL / MariaDB schema for XAMPP
--
-- The application runs this script automatically on startup
-- (CREATE ... IF NOT EXISTS), so importing it manually through
-- phpMyAdmin is optional.
-- ============================================================

CREATE DATABASE IF NOT EXISTS pet_grooming_inventory CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
USE pet_grooming_inventory;

CREATE TABLE IF NOT EXISTS categories (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(80) NOT NULL,
    UNIQUE KEY uq_categories_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS units (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(40) NOT NULL,
    UNIQUE KEY uq_units_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS suppliers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    contact_person VARCHAR(120) NOT NULL,
    phone VARCHAR(30) NOT NULL,
    address VARCHAR(255) NOT NULL,
    email VARCHAR(120) NULL,
    is_sample TINYINT(1) NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_suppliers_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- unit_id is legacy: the free-text description now holds size / flavor / variant.
-- The column and the units table stay so older data and backups remain valid.
CREATE TABLE IF NOT EXISTS items (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    category_id INT NOT NULL,
    pet_type VARCHAR(4) NOT NULL DEFAULT 'Both',
    quantity DECIMAL(12,2) NOT NULL DEFAULT 0,
    unit_id INT NULL,
    retail_price DECIMAL(12,2) NOT NULL DEFAULT 0,
    supplier_id INT NOT NULL,
    expiration_date DATE NULL,
    description VARCHAR(500) NULL,
    is_sample TINYINT(1) NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_items_name (name),
    CONSTRAINT chk_items_quantity CHECK (quantity >= 0),
    CONSTRAINT chk_items_price CHECK (retail_price >= 0),
    CONSTRAINT chk_items_pet_type CHECK (pet_type IN ('Dog', 'Cat', 'Both')),
    CONSTRAINT fk_items_category FOREIGN KEY (category_id) REFERENCES categories (id),
    CONSTRAINT fk_items_unit FOREIGN KEY (unit_id) REFERENCES units (id),
    CONSTRAINT fk_items_supplier FOREIGN KEY (supplier_id) REFERENCES suppliers (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Every quantity change (opening stock, stock-in, adjustment).
-- Item and supplier names are copied so history survives deletions.
CREATE TABLE IF NOT EXISTS stock_transactions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    item_id INT NULL,
    item_name VARCHAR(150) NOT NULL,
    txn_type VARCHAR(20) NOT NULL,
    qty_change DECIMAL(12,2) NOT NULL,
    qty_before DECIMAL(12,2) NOT NULL,
    qty_after DECIMAL(12,2) NOT NULL,
    supplier_id INT NULL,
    supplier_name VARCHAR(120) NULL,
    txn_date DATE NOT NULL,
    reason VARCHAR(60) NULL,
    note VARCHAR(255) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_stock_transactions_type (txn_type, created_at),
    CONSTRAINT fk_txn_item FOREIGN KEY (item_id) REFERENCES items (id) ON DELETE SET NULL,
    CONSTRAINT fk_txn_supplier FOREIGN KEY (supplier_id) REFERENCES suppliers (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Audit history shown on the Dashboard and the Activities page.
-- performed_by is reserved for when user accounts are added.
CREATE TABLE IF NOT EXISTS activity_log (
    id INT AUTO_INCREMENT PRIMARY KEY,
    subject_name VARCHAR(150) NOT NULL,
    activity_type VARCHAR(40) NOT NULL,
    description VARCHAR(500) NOT NULL,
    performed_by VARCHAR(80) NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_activity_log_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS settings (
    setting_key VARCHAR(60) NOT NULL PRIMARY KEY,
    setting_value VARCHAR(255) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Admin accounts. Admin is the only role; it is never taken from user input.
-- Passwords and security answers are stored as salted PBKDF2 hashes only.
-- Accounts are not part of backups, so a restore never locks the Admin out.
CREATE TABLE IF NOT EXISTS admin_accounts (
    id INT AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(100) NOT NULL,
    username VARCHAR(30) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    security_question VARCHAR(150) NOT NULL,
    security_answer_hash VARCHAR(255) NOT NULL,
    role VARCHAR(10) NOT NULL DEFAULT 'Admin',
    last_login_at DATETIME NULL,
    password_changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_admin_accounts_username (username),
    CONSTRAINT chk_admin_accounts_role CHECK (role = 'Admin')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Failed sign-in and password-recovery attempts, keyed by "login:<username>" / "recovery:<username>".
-- Works the same for usernames that do not exist, so a lockout reveals nothing about accounts.
CREATE TABLE IF NOT EXISTS auth_throttle (
    throttle_key VARCHAR(80) NOT NULL PRIMARY KEY,
    failed_count INT NOT NULL DEFAULT 0,
    first_failed_at DATETIME NOT NULL,
    locked_until DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Default lookup values (skipped when they already exist)
INSERT IGNORE INTO categories (name) VALUES
    ('Shampoo & Conditioner'),
    ('Grooming Tools'),
    ('Grooming Supplies'),
    ('Pet Food & Treats'),
    ('Medicine'),
    ('Vitamins'),
    ('Accessories'),
    ('Cleaning Supplies'),
    ('Other');

INSERT IGNORE INTO units (name) VALUES
    ('pieces'), ('bottle'), ('box'), ('pack'), ('bundle'), ('sachet'),
    ('can'), ('bag'), ('roll'), ('ml'), ('L'), ('g'), ('kg');

INSERT IGNORE INTO settings (setting_key, setting_value) VALUES
    ('low_stock_threshold', '10'),
    ('critical_stock_threshold', '5'),
    ('expiring_soon_days', '30'),
    ('app_name', 'PawStock');

-- Per-install secret used to pick a stand-in security question for unknown usernames.
INSERT IGNORE INTO settings (setting_key, setting_value) VALUES
    ('auth_decoy_key', SHA2(UUID(), 256));
