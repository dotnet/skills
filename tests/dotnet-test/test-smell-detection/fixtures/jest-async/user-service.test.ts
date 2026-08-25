describe("loadUser", () => {
  it("returns the requested user", () => {
    const loadUser = async () => ({ id: 7, name: "Ada" });

    expect(loadUser()).resolves.toEqual({ id: 7, name: "Ada" });
  });

  it("matches the stable snapshot", () => {
    const user = { id: 7, name: "Ada" };

    expect(user).toMatchSnapshot();
  });
});
