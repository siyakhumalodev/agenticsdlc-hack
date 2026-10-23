import axios from 'axios';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import type { Cart, CartItem } from './cart';
import { api } from './config';

/** Immutable paid prices in USD, not recalculated from the current catalog. */
export interface CheckoutReceipt {
  orderId: number;
  branchId: number;
  orderDate: string;
  status: 'confirmed';
  items: CartItem[];
  total: number;
  currency: 'usd';
}

/** Only the server's completed state proves payment; URL query flags are never trusted. */
export interface CheckoutStatus {
  state: 'none' | 'active' | 'expired' | 'completed';
  paymentUrl: string | null;
  order: CheckoutReceipt | null;
  snapshot: Cart | null;
}

interface CheckoutBranch {
  branchId: number;
  name: string;
}

/** Reconciles saved payment attempts; failed calls never clear the guest identity or cart. */
export function useCheckout(cartId: string | undefined) {
  const client = useQueryClient();
  const url = `${api.baseURL}${api.endpoints.carts}/${encodeURIComponent(cartId ?? '')}/checkout`;
  const key = ['checkout', cartId];
  const checkout = useQuery<CheckoutStatus, Error>(key,
    async () => (await axios.get<CheckoutStatus>(url)).data,
    { enabled: !!cartId, retry: false, refetchOnWindowFocus: false });
  const branches = useQuery<CheckoutBranch[], Error>('checkout-branches',
    async () => (await axios.get<CheckoutBranch[]>(`${api.baseURL}${api.endpoints.branches}`)).data,
    { enabled: !!cartId, retry: false });
  const mutation = useMutation<CheckoutStatus, Error, { kind: 'start'; branchId: number } | { kind: 'cancel' }>(
    async (action) => {
      if (!cartId) {throw new Error('No cart is available. Add products before checking out.');}
      const response = action.kind === 'start'
        ? await axios.post<CheckoutStatus>(url, { branchId: action.branchId })
        : await axios.post<CheckoutStatus>(`${url}/cancel`);
      return response.data;
    },
    {
      retry: false,
      onSuccess: async (state) => {
        client.setQueryData(key, state);
        await client.invalidateQueries('cart');
      },
    },
  );
  const refresh = async () => {
    mutation.reset();
    const result = await checkout.refetch();
    await client.invalidateQueries('cart');
    return result;
  };
  return { checkout, branches, mutation, refresh };
}
