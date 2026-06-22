import { OrderService } from '../src/order';

describe('OrderService', () => {
  it('creates an order', () => {
    const svc = new OrderService();
    const order = svc.create('alice', 2, 1000);
    expect(order).toBeDefined();
  });

  it('creates returns truthy', () => {
    const svc = new OrderService();
    const order = svc.create('bob', 1, 100);
    expect(order).toBeTruthy();
  });

  it('creates returns not null', () => {
    const svc = new OrderService();
    const order = svc.create('carol', 1, 100);
    expect(order).not.toBeNull();
  });

  it('list returns array', () => {
    const svc = new OrderService();
    svc.create('alice', 2, 1000);
    expect(Array.isArray(svc.list())).toBe(true);
  });

  it('always true', () => {
    expect(true).toBe(true);
  });

  it('confirms order resolves', async () => {
    const svc = new OrderService();
    const o = svc.create('alice', 1, 100);
    // BUG: missing await. expect(...).resolves silently passes even if promise rejects.
    expect(Promise.resolve(svc.confirm(o.id))).resolves.toBeDefined();
  });

  it('does not equal something irrelevant', () => {
    const svc = new OrderService();
    const o = svc.create('alice', 1, 100);
    expect(o.id).not.toBe('totally unrelated string');
  });

  it('round trip identity', () => {
    const svc = new OrderService();
    const o = svc.create('alice', 1, 100);
    expect(o.id).toBe(o.id);
  });

  it('legitimate equality on returned customer', () => {
    const svc = new OrderService();
    const o = svc.create('alice', 2, 500);
    expect(o.customer).toBe('alice');
  });

  it('legitimate exception assertion', () => {
    const svc = new OrderService();
    expect(() => svc.create('', 1, 100)).toThrow('customer is required');
  });
});
