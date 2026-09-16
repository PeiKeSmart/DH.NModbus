// 禁用程序集级别的并行测试，防止多个 TCP 服务端 Fixture 并行启动时产生端口竞争。
// 测试中存在多处固定端口（1502、1506）和随机端口（TOCTOU 竞争），顺序执行保证稳定性。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
