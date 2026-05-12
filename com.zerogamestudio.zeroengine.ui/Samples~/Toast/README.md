# Toast Sample

This sample demonstrates the default UGUI toast presenter.

Recommended project setup:

1. Create a top-level Canvas or reuse an existing UI root.
2. Add the generated `ToastRootPresenter` prefab under the UI root.
3. Keep one `ToastContainer` child per anchor lane you need.
4. Assign or reskin the generated `ToastItemView` prefab.
5. Call `Toast.Show(...)` from gameplay code.

Projects should wrap calls in a local adapter such as `POBAlert` or `GameAlert`.
