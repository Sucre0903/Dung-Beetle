using Grasshopper.Kernel;

namespace Sliding_Simulation
{
    public class DungBeetlePriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            // 将图标绑定到 "Dung Beetle" 这个分类标签上
            // 注意：第一个参数字符串必须与组件构造函数里的 Category 完全一致！
            Grasshopper.Instances.ComponentServer.AddCategoryIcon("Dung Beetle", Dung_Beetle.Properties.Resources.Dung_Beetle_icon2);

            // 可选：设置该分类的简写字母
            Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Dung Beetle", 'D');

            return GH_LoadingInstruction.Proceed;
        }
    }
}