# 数据库复制系统单元测试

本项目包含了数据库复制系统的完整单元测试套件，使用 xUnit 测试框架。

## 测试覆盖范围

### 1. DatabaseBasedFollowerReplicatorTests
- 测试数据库复制器的核心功能
- 构造函数参数验证
- 启动和停止复制服务
- 资源释放和清理

### 2. TableConfigTests
- 测试表配置类的功能
- 默认值验证
- 属性设置和获取
- 复制方向和间隔设置

### 3. DirectTimeSynchronizationServiceTests
- 测试时间同步服务
- 系统时间获取
- 权限检查
- 时间差异计算
- 参数验证

### 4. LoggerTests
- 测试日志记录功能
- 单例模式验证
- 多线程并发测试
- 特殊字符处理
- 异常处理

### 5. DatabaseReplicationConfigTests
- 测试配置管理功能
- JSON 序列化和反序列化
- 配置属性验证
- 表配置管理

### 6. EnumsTests
- 测试所有枚举类型
- 枚举值验证
- 字符串转换测试

## 运行测试

### 方法一：使用批处理脚本
```bash
# 在项目根目录运行
run-tests.bat
```

### 方法二：使用 .NET CLI
```bash
# 构建解决方案
dotnet build DataBaseAsync.sln

# 运行所有测试
dotnet test DataBaseAsync.Tests\DataBaseAsync.Tests.csproj

# 运行测试并生成详细报告
dotnet test DataBaseAsync.Tests\DataBaseAsync.Tests.csproj --logger "console;verbosity=detailed"

# 运行测试并生成代码覆盖率报告
dotnet test DataBaseAsync.Tests\DataBaseAsync.Tests.csproj --collect:"XPlat Code Coverage"
```

### 方法三：使用 Visual Studio
1. 打开 `DataBaseAsync.sln` 解决方案
2. 在测试资源管理器中查看所有测试
3. 右键点击测试项目或单个测试运行

## 测试依赖

- **xUnit**: 测试框架
- **Moq**: 模拟对象框架
- **Microsoft.EntityFrameworkCore.InMemory**: 内存数据库用于测试
- **Microsoft.NET.Test.Sdk**: .NET 测试 SDK

## 测试数据

测试使用以下策略来避免对真实数据库的依赖：
- 使用内存数据库进行 Entity Framework 测试
- 使用模拟对象（Mock）来隔离外部依赖
- 使用临时文件和目录进行文件操作测试

## 注意事项

1. **权限测试**: `DirectTimeSynchronizationServiceTests` 中的某些测试可能需要管理员权限
2. **数据库连接**: 某些测试可能会尝试连接数据库，如果连接失败会抛出预期的异常
3. **并发测试**: `LoggerTests` 包含多线程测试，确保日志记录的线程安全性
4. **清理**: 测试会自动清理临时文件和资源

## 持续集成

这些测试可以集成到 CI/CD 管道中：

```yaml
# GitHub Actions 示例
- name: Run Tests
  run: |
    dotnet build DataBaseAsync.sln --configuration Release
    dotnet test DataBaseAsync.Tests/DataBaseAsync.Tests.csproj --no-build --configuration Release --logger trx --results-directory TestResults
```

## 扩展测试

要添加新的测试：

1. 在相应的测试类中添加新的测试方法
2. 使用 `[Fact]` 属性标记简单测试
3. 使用 `[Theory]` 和 `[InlineData]` 属性标记参数化测试
4. 遵循 AAA 模式（Arrange, Act, Assert）
5. 确保测试方法名称清晰描述测试意图

## 测试报告

运行测试后，可以查看：
- 控制台输出的测试结果
- 代码覆盖率报告（如果启用）
- Visual Studio 测试资源管理器中的详细结果