# 架构决策记录

| ADR | 状态 | 决策 |
|---|---|---|
| [0001](0001-modular-monolith-and-dotnet.md) | Accepted | 使用 .NET 8 模块化单体，核心与 UI 解耦 |
| [0002](0002-content-hash-as-authority.md) | Accepted | SHA-256 为正式一致性依据 |
| [0003](0003-declarative-patch-and-transactional-apply.md) | Accepted | 声明式补丁和事务式应用 |
| [0004](0004-no-symlink-following-in-v1.md) | Accepted | v1 不跟随重解析点 |

新增重要架构决策必须创建新 ADR，不修改历史决策来掩盖变化。被替代的 ADR 应标记 Superseded 并链接新记录。
