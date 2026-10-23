import axios from 'axios';
import { api } from './config';

/** Monetary fields are server-calculated dollars, rounded to cents after discount. */
export interface CartItem {
  productId: number;
  name: string;
  imgName: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

/** The identifier is a bearer capability; never log it or place it in browser navigation. */
export interface Cart {
  cartId: string;
  items: CartItem[];
  total: number;
}

/** Add increments a quantity; update replaces it. Reset forgets an inaccessible guest cart. */
export type CartAction =
  | { kind: 'add'; productId: number; quantity: number }
  | { kind: 'update'; productId: number; quantity: number }
  | { kind: 'remove'; productId: number }
  | { kind: 'reset' };

const baseUrl = `${api.baseURL}${api.endpoints.carts}`;
export const CART_STORAGE_KEY = 'octocat.cartId';
let mutationQueue: Promise<void> = Promise.resolve();

/** Reads only the guest identifier; storage access errors propagate to the UI. */
export function readCartId(): string | null {
  return localStorage.getItem(CART_STORAGE_KEY);
}

/** Fetches the persisted snapshot without converting failures into an empty cart. */
export async function fetchCart(cartId: string): Promise<Cart> {
  return (await axios.get<Cart>(`${baseUrl}/${encodeURIComponent(cartId)}`)).data;
}

async function executeAction(action: CartAction): Promise<Cart | null> {
  if (action.kind === 'reset') {
    localStorage.removeItem(CART_STORAGE_KEY);
    return null;
  }
  let cartId = readCartId();
  if (!cartId) {
    if (action.kind !== 'add') {throw new Error('No cart is available. Add a product to start a cart.');}
    const created = (await axios.post<Cart>(baseUrl)).data;
    localStorage.setItem(CART_STORAGE_KEY, created.cartId);
    cartId = created.cartId;
  }
  const url = `${baseUrl}/${encodeURIComponent(cartId)}/items`;
  if (action.kind === 'add') {
    return (await axios.post<Cart>(url, { productId: action.productId, quantity: action.quantity })).data;
  }
  if (action.kind === 'update') {
    return (await axios.put<Cart>(`${url}/${action.productId}`, { quantity: action.quantity })).data;
  }
  await axios.delete(`${url}/${action.productId}`);
  return fetchCart(cartId);
}

/** Serializes mutations (including lazy creation) across hook instances; never retries a write. */
export function mutateCart(action: CartAction): Promise<Cart | null> {
  const result = mutationQueue.then(() => executeAction(action));
  // Keep the queue usable after failure; the original result still rejects to its caller.
  mutationQueue = result.then(() => undefined, () => undefined);
  return result;
}

/** Provides safe actionable text without exposing request URLs or bearer tokens. */
export function cartErrorMessage(error: unknown): string {
  if (axios.isAxiosError<{ error?: { message?: string } }>(error)) {
    return error.response?.data?.error?.message ?? 'Cannot reach your cart. Check the cart before retrying.';
  }
  if (error instanceof Error) {
    return error.message;
  }
  return 'Cannot access your cart. Check browser storage permissions and retry.';
}
