# Changelog

## 1.0.1 - 2025-06-26

- Convert all ids to use `long` instead of `int`
- Add `CreateContext` and `GetContext` to `EffectBuilder`
- Skip execution of Memos with an ancestor scheduled to remount
- Fix issue with `SpokeSingleton` not cleaning up when its scene is unloaded

## 1.0.0 - 2025-05-17

- Initial public release
- Core reactive engine (`Spoke.cs`)
- Unity integration (`Spoke.Unity.cs`)
- Example usage and documentation
