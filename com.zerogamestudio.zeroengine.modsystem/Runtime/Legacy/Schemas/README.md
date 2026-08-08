# ZeroEngine ModSystem Schemas

这个目录包含用于验证mod内容文件的JSON Schema。

## 使用方法

在你的JSON文件开头添加 `$schema` 引用即可获得IDE验证支持：

```json
{
    "$schema": "../Schemas/manifest.schema.json",
    "Id": "my.awesome.mod",
    ...
}
```

## 可用Schemas

| 文件 | 用途 |
|------|------|
| `manifest.schema.json` | Mod清单文件 (manifest.json) |
| `ability.schema.json` | 技能数据 (AbilityDataSO) |
| `buff.schema.json` | Buff数据 (BuffData) |
