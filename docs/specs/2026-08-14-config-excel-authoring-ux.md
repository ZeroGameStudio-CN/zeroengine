# 通用配置 Excel 策划工作簿 UX

- 状态：Implemented
- 更新日期：2026-08-14
- 基线：`49251f09005cd21c579cf3475833b3caccdff430` 加当前未提交的通用导航 Sheet 实现
- 执行授权：已授权；来源为用户要求优化布局、筛选与操作体验并“修审到毕业”
- 终态提交授权：已授权；来源为用户要求“修审到毕业”

## 目标与非目标

把通用配置流水线生成的 XLSX 做成适合策划长期批量录入的工作簿：容易找到业务 Sheet、容易筛选和追加记录、字段含义与必填状态清楚，同时保持可靠的双向导入导出。

不引入宏、脚本按钮、COM 运行时依赖或搜打撤专用名称；不改变 Schema、JSON、行列数据契约，也不修改任何项目生产配置工作簿。

## 当前与目标行为

当前工作簿已有“配置目录”、内部跳转、隐藏技术 Sheet 和基础枚举下拉，但业务 Sheet 仍接近原始网格，目录不可筛选，新增行和字段约束提示不足。

目标工作簿由生成器稳定产生：

- “配置目录”默认打开，冻结标题区，提供原生筛选和 Sheet 内部跳转。
- 每个业务 Sheet 使用原生 Excel Table 承载标题、筛选、条纹行和自动扩展的新行；业务 Sheet 不启用会阻断 Table 扩行的工作表保护，第一行机器字段仍隐藏并保持导入契约。
- 业务标题统一配色，必填字段显示 `＊`，首列保留返回目录链接；网格线隐藏、冻结前两行、列宽按字段类型设定。
- 枚举、布尔和有边界的整数/小数使用 Excel 数据校验；最终导入校验仍以 Schema/Check 为准。

## 决策、约束与兼容性

- Excel 是策划编辑界面；生成器、Reader 和 Unity Check/Plan/Apply 是权威处理链路。
- 使用无宏的 XLSX 原生能力：Table、AutoFilter、冻结窗格、数据校验和内部超链接。
- 技术 Sheet 继续 `VeryHidden` 且受保护；业务 Sheet 保持可增删行，依靠隐藏机器表头、数据校验与 Unity Check 门禁保护契约。
- 旧工作簿没有“配置目录”或 Table 时仍可导入；Reader 不依赖展示样式或 Table。
- 目录和表名从 Schema 动态生成，只保留通用保留名与合法化规则。
- 单元格公式及 Table 的计算列/汇总公式、外部链接、VBA 和未知业务 Sheet 继续拒绝；数据校验引用和内部命名区域不是业务计算公式，继续允许。
- Schema 字符串列使用 Excel 文本格式，策划输入 `00123` 等标识符时不得被 Excel 自动转成数字。
- Table 展示表头在添加必填标识、目录前缀和去重后仍不得超过 Excel 的 255 字符限制；完整机器字段名和 Schema 元数据保持不变。
- 可选“配置目录”不占用原有业务/内部 Sheet 安全配额；未知或重复 Sheet 仍计入并拒绝，从而保持旧上限边界兼容。
- 回滚仅需恢复生成器；已生成工作簿的数据行仍符合原有 Reader 契约。

## 范围

- `com.zerogamestudio.zeroengine.config-pipeline/Editor/Excel/XlsxConfigWorkbookWriter.cs`
- `com.zerogamestudio.zeroengine.config-pipeline/Editor/Excel/XlsxConfigSourceReader.cs`
- `com.zerogamestudio.zeroengine.config-pipeline/Tests/Editor/XlsxWorkbookTests.cs`
- 本规格

## 验证与验收

1. 生成的 XLSX 通过 OpenXML Validator，并能由桌面 Excel 正常打开且不生成修复日志。
2. 默认活动页是“配置目录”；目录冻结前四行，可筛选，可点击进入任意 Schema 声明的业务 Sheet。
3. 业务 Sheet 冻结前两行、隐藏网格线，显示筛选按钮、条纹行、必填标识和返回目录链接；可直接追加/删除行，不受工作表保护阻断，也不需要维护生成器代码。
4. 枚举、布尔、整数/小数边界均生成与 Schema 一致的数据校验。
5. 新工作簿完整往返不改变规范化配置；旧版无目录工作簿仍能导入。
6. 工作簿不含单元格公式或 Table 计算/汇总公式、外部链接或宏；注入任一 Table 公式后 Reader 以 `XLSX_FORMULA_FORBIDDEN` 拒绝；生产配置文件零修改。
7. Schema 字符串列使用文本格式；桌面 Excel 输入 `00123`、保存并重开后仍为 `00123`，Reader 能按字符串导入。
8. 任意 Schema 展示标题生成的 Table 列名不超过 255 字符，超长标题工作簿通过 OpenXML Validator 并能由桌面 Excel 正常打开。
9. 原有 Sheet 上限边界在增加可选目录后仍可读取；未知或重复 Sheet 仍拒绝。
10. `ZeroGameStudio.ConfigPipeline.Tests.XlsxWorkbookTests` 全部通过，相关 Unity 编译为零错误，Console Error=0。

## 实施证据

- OpenXML Validator：0 error；新旧工作簿往返、目录、Table、长表头与公式拒绝均已覆盖。
- 桌面 Excel：默认打开“配置目录”；目录与业务 Sheet 可筛选、跳转、追加行；字符串 `00123` 保存重开后保持不变且 Reader 可导入；业务 Sheet 未受保护，技术 Sheet 仍受保护。
- 独立 .NET 回归：15/15；Unity EditMode `ZeroGameStudio.ConfigPipeline.Tests.XlsxWorkbookTests`：15/15。
- Unity 编译：0 error；Console：0 Error；未修改任何生产配置工作簿。
