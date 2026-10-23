import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { cartErrorMessage } from '../../../api/cart';
import type { CartItem } from '../../../api/cart';
import { useCart } from '../../../api/useCart';
import { useTheme } from '../../../context/ThemeContext';

const money = (amount: number) => `$${amount.toFixed(2)}`;
const focusStyle = 'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';
const buttonStyle = `min-h-11 px-4 py-2 rounded-lg border border-current disabled:opacity-50 ${focusStyle}`;

/** Presents persisted guest items; prices and totals always come from the API. */
export default function CartView() {
  const { darkMode } = useTheme();
  const cart = useCart();
  const heading = useRef<HTMLHeadingElement>(null);
  const [message, setMessage] = useState('');
  useEffect(() => {
    window.scrollTo(0, 0);
    heading.current?.focus({ preventScroll: true });
  }, []);

  return (
    <div className={`min-h-screen pt-24 pb-16 px-4 ${darkMode ? 'bg-dark text-light' : 'bg-gray-100 text-gray-800'}`}>
      <div className="max-w-5xl mx-auto space-y-6">
        <div className="flex flex-wrap items-baseline justify-between gap-4">
          <h1 ref={heading} tabIndex={-1} className="text-3xl font-bold">Your cart</h1>
          <Link to="/products" className={`underline rounded ${focusStyle}`}>Continue shopping</Link>
        </div>
        <p className={darkMode ? 'text-gray-300' : 'text-gray-600'}>
          Your cart is saved for this browser. Prices include current product discounts.
        </p>
        <p role="status" aria-live="polite">{message}</p>
        {cart.isLoading && <p role="status">Loading your cart...</p>}
        {cart.error && (
          <div role="alert" className={`rounded-lg border p-4 space-y-3 ${darkMode ? 'text-red-300' : 'text-red-700'}`}>
            <p>{cart.unavailable ? 'This saved cart is no longer available. Start a new cart to continue.' : cartErrorMessage(cart.error)}</p>
            {cart.unavailable ? (
              <button disabled={cart.busy} className={buttonStyle}
                onClick={() => cart.mutation.mutate({ kind: 'reset' }, { onSuccess: () => setMessage('New cart ready. Add products to get started.') })}>
                Start new cart
              </button>
            ) : (
              <button disabled={cart.busy} className={buttonStyle} onClick={() => void cart.retry()}>Retry loading cart</button>
            )}
          </div>
        )}
        {!cart.isLoading && !cart.error && (cart.cart?.items.length ?? 0) === 0 && (
          <div className={`rounded-xl border p-8 ${darkMode ? 'border-gray-700 bg-gray-800' : 'border-gray-200 bg-white'}`}>
            <h2 className="text-xl font-semibold mb-2">Your cart is empty</h2>
            <p>Choose products from the catalog to get started.</p>
          </div>
        )}
        {!cart.unavailable && cart.cart && (
          <ul aria-label="Cart items" className="space-y-4">
            {cart.cart.items.map((item) => (
              <CartLine key={item.productId} item={item} darkMode={darkMode}
                busy={cart.busy || !!cart.error}
                onUpdate={(quantity) => {
                  setMessage('');
                  cart.mutation.mutate({ kind: 'update', productId: item.productId, quantity }, {
                    onSuccess: () => setMessage(`Updated quantity of ${item.name}.`),
                  });
                }}
                onRemove={() => {
                  setMessage('');
                  cart.mutation.mutate({ kind: 'remove', productId: item.productId }, {
                    onSuccess: () => { setMessage(`Removed ${item.name}.`); heading.current?.focus(); },
                  });
                }} />
            ))}
          </ul>
        )}
        {!cart.isLoading && !cart.error && (
          <div className={`flex justify-between items-baseline border-t-2 border-primary pt-5 ${darkMode ? 'text-light' : 'text-gray-800'}`}>
            <h2 className="text-xl font-semibold">Cart total</h2>
            <p aria-label="Cart total" className="text-3xl font-bold tabular-nums">{money(cart.cart?.total ?? 0)}</p>
          </div>
        )}
        {!!cart.cart?.items.length && !cart.unavailable && (
          <div className="flex justify-end">
            <Link to="/checkout" className={`${buttonStyle} font-semibold`}>Proceed to checkout</Link>
          </div>
        )}
      </div>
    </div>
  );
}

function CartLine({ item, busy, darkMode, onUpdate, onRemove }: {
  item: CartItem;
  busy: boolean;
  darkMode: boolean;
  onUpdate: (quantity: number) => void;
  onRemove: () => void;
}) {
  const [quantity, setQuantity] = useState(String(item.quantity));
  return (
    <li className={`rounded-xl border p-5 flex flex-col sm:flex-row gap-5 ${darkMode ? 'border-gray-700 bg-gray-800' : 'border-gray-200 bg-white'}`}>
      {item.imgName && <img src={`/${item.imgName}`} alt="" width={96} height={96} className="w-24 h-24 object-contain self-start" />}
      <div className="flex-1 min-w-0 space-y-4">
        <div className="flex flex-wrap justify-between gap-2">
          <div>
            <h2 className="text-xl font-semibold break-words">{item.name}</h2>
            <p className={darkMode ? 'text-gray-300' : 'text-gray-600'}>{money(item.unitPrice)} each</p>
          </div>
          <p aria-label={`Line total for ${item.name}`} className="text-xl font-semibold tabular-nums">{money(item.lineTotal)}</p>
        </div>
        <form className="flex flex-wrap items-end gap-3" onSubmit={(event) => {
          event.preventDefault();
          const value = Number(quantity);
          if (Number.isInteger(value) && value > 0 && value <= 2147483647) {onUpdate(value);}
        }}>
          <label className="flex flex-col gap-1 text-sm">
            Quantity for {item.name}
            <input type="number" min="1" max="2147483647" step="1" required value={quantity}
              disabled={busy} onChange={(event) => setQuantity(event.target.value)}
              className={`w-28 rounded-lg border min-h-11 p-2 ${focusStyle} ${darkMode ? 'bg-gray-900 border-gray-600' : 'bg-white border-gray-400'}`} />
          </label>
          <button disabled={busy} type="submit" className={buttonStyle} aria-label={`Update quantity of ${item.name}`}>Update quantity</button>
          <button disabled={busy} type="button" className={buttonStyle} onClick={onRemove} aria-label={`Remove ${item.name}`}>Remove</button>
        </form>
      </div>
    </li>
  );
}
