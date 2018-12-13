# Cs2LuaRewrite
配合CSharp代码转lua，对CSharp工程代码进行重写

【命令行】

    Cs2LuaRewriter [-rootns Cs2LuaScript] [-out dir] [-outputresult] [-d macro] [-u macro] [-refbyname dllname alias] [-refbypath dllpath alias] [-systemdllpath dllpath] [-src] csfile|csprojfile

其中:

    macro = 宏定义，会影响被转化的c#代码里的#if/#elif/#else/#endif语句的结果。

    dllname = 以名字（Assembly Name）提供的被引用的外部dotnet DLL，cs2lua尝试从名字获取这些DLL的路径（一般只有dotnet系统提供的DLL才可以这么用）。

    dllpath = 以文件全路径提供的被引用的外部dotnet DLL。

    alias = 外部dll顶层名空间别名，默认为global, 别名在c#代码里由'extern alias 名字;'语句使用。

    outputresult = 此选项指明是否在控制台输出最终转化的结果（合并为单一文件样式）。

    src = 此选项仅用在refbyname/refbypath选项未指明alias参数的情形，此时需要此选项在csfile|csprojfile前明确表明后面的参数是输入文件。
