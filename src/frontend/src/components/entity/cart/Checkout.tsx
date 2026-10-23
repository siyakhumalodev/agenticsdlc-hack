import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { cartErrorMessage } from '../../../api/cart';
import { useCheckout } from '../../../api/checkout';
import { useCart } from '../../../api/useCart';
import { useTheme } from '../../../context/ThemeContext';

const money = (value: number) => `$${value.toFixed(2)}`;
const focus = 'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';
const button = `min-h-11 rounded-lg border border-current px-4 py-2 disabled:opacity-50 ${focus}`;

/** Reviews server-priced items and resumes hosted payment without collecting card data. */
export default function Checkout() {
  const { darkMode } = useTheme();
  const cart = useCart();
  const { checkout, branches, mutation, refresh } = useCheckout(cart.cartId);
  const [branchId, setBranchId] = useState('');
  const heading = useRef<HTMLHeadingElement>(null);
  const state = mutation.data ?? checkout.data;
  const receipt = state?.order;
  const summary = state?.state === 'active' ? state.snapshot : cart.cart;
  const showReceipt = state?.state === 'completed' && receipt && (cart.cart?.items.length ?? 0) === 0;
  const error = mutation.error ?? checkout.error ?? cart.error;
  const busy = mutation.isLoading || checkout.isFetching || cart.busy;
  const panel = darkMode ? 'bg-gray-800 border-gray-700' : 'bg-white border-gray-200';

  useEffect(() => {
    heading.current?.focus();
    window.scrollTo(0, 0);
  }, [showReceipt]);

  return (
    <div className={`min-h-screen pt-24 pb-16 px-4 ${darkMode ? 'bg-dark text-light' : 'bg-gray-100 text-gray-800'}`}>
      <div className="max-w-5xl mx-auto space-y-6">
        <Link to="/cart" className={`underline rounded ${focus}`}>Back to cart</Link>
        <div className="border-l-4 border-primary pl-5">
          <p className="text-sm font-mono uppercase tracking-widest">{showReceipt ? 'Purchase confirmed' : 'Secure checkout'}</p>
          <h1 ref={heading} tabIndex={-1} className="text-3xl font-bold mt-2">
            {showReceipt ? `Order #${receipt.orderId}` : 'Review. Pay. Ready.'}
          </h1>
          <p className="mt-2">{showReceipt ? 'Payment received. Your order is confirmed.' : 'Review your supplies, choose a branch, then pay securely with Stripe.'}</p>
        </div>
        {(cart.isLoading || checkout.isLoading) && <p role="status">Checking your cart and payment...</p>}
        {error && (
          <div role="alert" className={`rounded-lg border p-4 space-y-3 ${darkMode ? 'text-red-300' : 'text-red-700'}`}>
            <p>{cartErrorMessage(error)}</p>
            <p>Your cart remains saved. Check payment before starting another checkout.</p>
            <button type="button" className={button} disabled={busy} onClick={() => {
              void cart.retry().then(() => refresh());
            }}>Check payment again</button>
          </div>
        )}
        {!error && !cart.isLoading && !showReceipt && !cart.cart?.items.length && (
          <div className={`rounded-xl border p-6 ${panel}`}>
            <p>Your cart is empty. Add products before checking out.</p>
            <Link to="/products" className={`underline ${focus}`}>Browse products</Link>
          </div>
        )}
        {(showReceipt || !!summary?.items.length) && (
          <div className="grid gap-6 md:grid-cols-[minmax(0,1fr)_minmax(16rem,0.8fr)]">
            <section aria-label={showReceipt ? 'Order details' : 'Purchase summary'} className={`rounded-xl border p-6 ${panel}`}>
              <h2 className="text-xl font-semibold mb-5">{showReceipt ? 'Order details' : 'Your supplies'}</h2>
              <ul className="divide-y divide-gray-500">
                {(showReceipt ? receipt.items : summary?.items ?? []).map((item) => (
                  <li key={item.productId} className="py-4 flex justify-between gap-4">
                    <div className="min-w-0">
                      <p className="font-semibold break-words">{item.name}</p>
                      <p className="text-sm">{item.quantity} × {money(item.unitPrice)}</p>
                    </div>
                    <span className="font-mono whitespace-nowrap">{money(item.lineTotal)}</span>
                  </li>
                ))}
              </ul>
              <div className="border-t-2 border-primary mt-3 pt-4 flex justify-between gap-4">
                <span>{showReceipt ? 'Total paid (USD)' : 'Total (USD)'}</span>
                <strong className="font-mono text-xl">{money(showReceipt ? receipt.total : summary?.total ?? 0)}</strong>
              </div>
            </section>
            <section aria-label={showReceipt ? 'Order confirmation' : 'Payment'} className={`rounded-xl border p-6 space-y-5 ${panel}`}>
              {showReceipt ? (
                <>
                  <h2 className="text-xl font-semibold">Confirmed</h2>
                  <p>Branch #{receipt.branchId}</p>
                  <p><time dateTime={receipt.orderDate}>{new Date(receipt.orderDate).toLocaleString()}</time></p>
                  <Link to="/products" className={`inline-block ${button}`}>Continue shopping</Link>
                </>
              ) : state?.state === 'active' ? (
                <>
                  <h2 className="text-xl font-semibold">Complete your payment</h2>
                  <p>Your cart is saved and reserved for this checkout. A declined card does not create an order. Retry on Stripe or cancel to edit your cart.</p>
                  {state.paymentUrl && <a className={`inline-block ${button}`} href={state.paymentUrl}>Continue to secure payment</a>}
                  <button className={button} disabled={busy} onClick={() => void refresh()}>Check payment again</button>
                  <button className={button} disabled={busy} onClick={() => mutation.mutate({ kind: 'cancel' })}>Cancel payment and edit cart</button>
                </>
              ) : (
                <form className="space-y-5" onSubmit={(event) => {
                  event.preventDefault();
                  mutation.mutate({ kind: 'start', branchId: Number(branchId) });
                }}>
                  <h2 className="text-xl font-semibold">Payment</h2>
                  {state?.state === 'expired' && <p role="status">Payment was canceled or expired. Your items are still in the cart.</p>}
                  {branches.error && <p role="alert">{cartErrorMessage(branches.error)} <button type="button" className="underline" onClick={() => void branches.refetch()}>Retry branches</button></p>}
                  <label className="block space-y-2">
                    <span>Order branch</span>
                    <select required value={branchId} disabled={busy || branches.isLoading}
                      onChange={(event) => setBranchId(event.target.value)}
                      className={`block w-full min-h-11 rounded-lg border p-2 ${focus} ${darkMode ? 'bg-gray-900 border-gray-600' : 'bg-white border-gray-400'}`}>
                      <option value="">Choose a branch</option>
                      {branches.data?.map((branch) => <option key={branch.branchId} value={branch.branchId}>{branch.name}</option>)}
                    </select>
                  </label>
                  <p className="text-sm">Card details are entered on Stripe, not stored by OctoCAT. Your order is confirmed only after payment is verified.</p>
                  <button type="submit" className={`${button} font-semibold`} disabled={busy || !!error || !branchId || !!branches.error}>
                    {mutation.isLoading ? 'Preparing payment...' : 'Start secure payment'}
                  </button>
                </form>
              )}
            </section>
          </div>
        )}
      </div>
    </div>
  );
}
