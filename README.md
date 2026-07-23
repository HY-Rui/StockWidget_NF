
# StockWidget C# 重写版

这是 StockWidget 的 C# 重写版本，用于在 Windows 桌面悬浮显示股票和国内期货行情。

## 主要改动

### 行情与显示

- 添加期货行情显示；
- 刷新间隔支持 `0.1` 秒至 `60` 秒。
- 优化一些布局。

### 自选与持仓

- 双击或右键自选可编辑代码、持仓成本、持仓数量、多或做空。
- 添加期货行情显示
- 刷新间隔支持 `0.1` 秒至 `60` 秒。
- 有持仓的自选使用红色标记。
- 持仓汇总会计算全部自选中的持仓，不受该品种是否勾选显示影响。
- 内置 87 个期货品种参数，可在“品种参数”中查看和修改。

### 设置与窗口行为

- 设置窗口和悬浮窗都会记住上次位置，并限制在当前屏幕工作区内。
- 支持可配置的边缘吸附及吸附距离，拖动时不会让窗口侧边移出桌面。
- 左键双击悬浮窗打开设置；右键可切换指标、刷新、自适应列宽和鼠标穿透。
- 鼠标穿透同时提供于悬浮窗右键菜单和任务栏托盘图标菜单。

## 项目结构

```text
StockWidgetCSharp/
|-- data/       新浪股票和期货行情解析
|-- models/     配置、行情、持仓及期货参数模型
|-- services/   行情刷新与全局热键服务
|-- views/      悬浮窗、设置、自选编辑、K 线等界面
|-- App.xaml    应用入口与托盘管理
`-- StockWidgetCSharp.csproj
```

## 构建

开发环境需要 Windows 10/11 x64 和 .NET 9 SDK。

```powershell
dotnet build .\StockWidgetCSharp\StockWidgetCSharp.csproj -c Release
```

发布 self-contained `win-x64` 版本：

```powershell
dotnet publish .\StockWidgetCSharp\StockWidgetCSharp.csproj -c Release -o .\dist\StockWidgetCSharp
```

发布版本自带 .NET 运行环境，目标电脑无需另行安装 .NET 9。

## 配置

配置文件继续使用原项目路径：

```text
%APPDATA%\StockWidget\SW_config.json
```

程序兼容旧版主要 JSON 配置键，并分别保存股票/期货自选、显示项、持仓、期货品种参数、窗口位置和外观设置。

## 数据说明

- 行情数据来自新浪公开行情接口，程序运行时需要网络连接。
- 国内期货盈亏依赖对应品种的每跳价差和每跳毛利；缺少有效参数时以 `---` 显示，避免给出错误结果。
