CREATE TABLE IF NOT EXISTS carts (
    cart_id INTEGER PRIMARY KEY,
    token TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS cart_items (
    cart_id INTEGER NOT NULL,
    product_id INTEGER NOT NULL,
    quantity INTEGER NOT NULL CHECK (typeof(quantity) = 'integer' AND quantity BETWEEN 1 AND 2147483647),
    PRIMARY KEY (cart_id, product_id),
    FOREIGN KEY (cart_id) REFERENCES carts(cart_id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON UPDATE CASCADE ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_cart_items_product_id ON cart_items(product_id);

-- Manual rollback: drop cart_items, then carts. This discards guest carts.
