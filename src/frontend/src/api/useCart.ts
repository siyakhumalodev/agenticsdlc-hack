import axios from 'axios';
import { useIsMutating, useMutation, useQuery, useQueryClient } from 'react-query';
import { fetchCart, mutateCart, readCartId } from './cart';
import type { Cart, CartAction } from './cart';

/** Shares server snapshots by guest identity, serializes writes, and refetches uncertain failures. */
export function useCart() {
  const client = useQueryClient();
  const identity = useQuery<string | null, Error>('cart-id', readCartId, {
    staleTime: Infinity,
    retry: false,
  });
  const cartId = identity.data;
  const cart = useQuery<Cart, Error>(['cart', cartId], () => {
    if (!cartId) {throw new Error('No cart identifier is available.');}
    return fetchCart(cartId);
  }, { enabled: !!cartId, retry: false });
  const pending = useIsMutating('cart-mutation');
  const mutation = useMutation<Cart | null, Error, CartAction>('cart-mutation', mutateCart, {
    retry: false,
    onSuccess: async (snapshot) => {
      if (snapshot) {
        client.setQueryData(['cart', snapshot.cartId], snapshot);
        client.setQueryData('cart-id', snapshot.cartId);
      } else {
        client.setQueryData('cart-id', null);
        client.removeQueries('cart');
      }
    },
    onError: async () => {
      // A failed response may follow a committed write. Recover the authoritative state before retry.
      await client.invalidateQueries('cart-id');
      await client.invalidateQueries('cart');
    },
  });
  const unavailable = axios.isAxiosError(cart.error) &&
    (cart.error.response?.status === 404 || cart.error.response?.status === 400);

  return {
    cartId: cartId ?? undefined,
    cart: cart.data,
    error: mutation.error ?? identity.error ?? cart.error,
    isLoading: identity.isLoading || (!!cartId && cart.isLoading),
    busy: pending > 0 || identity.isLoading || cart.isFetching,
    unavailable,
    mutation,
    retry: async () => {
      mutation.reset();
      await client.invalidateQueries('cart-id');
      await client.invalidateQueries('cart');
    },
  };
}
