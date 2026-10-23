CREATE TABLE IF NOT EXISTS checkouts (
    checkout_id TEXT PRIMARY KEY,
    cart_id INTEGER NOT NULL,
    branch_id INTEGER NOT NULL,
    snapshot TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    session_id TEXT UNIQUE,
    state TEXT NOT NULL DEFAULT 'active' CHECK (state IN ('active', 'expired', 'completed')),
    receipt TEXT,
    FOREIGN KEY (cart_id) REFERENCES carts(cart_id) ON UPDATE CASCADE ON DELETE RESTRICT,
    FOREIGN KEY (branch_id) REFERENCES branches(branch_id) ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_checkouts_active_cart ON checkouts(cart_id) WHERE state = 'active';
CREATE INDEX IF NOT EXISTS idx_checkouts_cart ON checkouts(cart_id);
CREATE INDEX IF NOT EXISTS idx_checkouts_branch ON checkouts(branch_id);

CREATE TABLE IF NOT EXISTS checkout_products (
    checkout_id TEXT NOT NULL,
    product_id INTEGER NOT NULL,
    PRIMARY KEY (checkout_id, product_id),
    FOREIGN KEY (checkout_id) REFERENCES checkouts(checkout_id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON UPDATE CASCADE ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS idx_checkout_products_product ON checkout_products(product_id);

-- Manual rollback: stop payment traffic and reconcile active sessions before dropping
-- checkout_products and checkouts. Dropping them loses payment deduplication and receipts.
