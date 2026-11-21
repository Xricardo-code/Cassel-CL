using System;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

// Build-time policy. Only launcher self-update entry points are changed.
// Game downloads, mod updates, server configuration and export code are preserved.
internal static class LauncherUpdatePolicy
{
    public const int ChangedMethodCount = 8;
    static MethodDef Method(ModuleDef module,string type,string name)
    {
        var owner=module.Find(type,false);
        if(owner==null) throw new InvalidOperationException("Missing update-policy type: "+type);
        var matches=owner.Methods.Where(m=>m.Name==name).ToArray();
        if(matches.Length!=1 || !matches[0].HasBody) throw new InvalidOperationException("Unexpected update-policy method: "+type+"."+name);
        return matches[0];
    }
    static void ReturnOnly(MethodDef method)
    {
        if(method.ReturnType.ElementType!=ElementType.Void) throw new InvalidOperationException("Expected void method: "+method.FullName);
        method.Body=new CilBody(); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
    public static void Apply(ModuleDef module,bool report)
    {
        foreach(string name in new[]{"UpdateCheckByButton","UpdateStart","UpdateRestart","UpdateReplace"})
            ReturnOnly(Method(module,"PCL.ModSecret",name));
        ReturnOnly(Method(module,"PCL.FormMain","ShowUpdateLog"));
        ReturnOnly(Method(module,"PCL.PageSetupSystem","ComboSystemUpdate_SelectionChanged"));

        // The custom launcher does not participate in upstream version-expiry prompts.
        var status=Method(module,"PCL.PageSetupSystem","IsLauncherNewest");
        if(status.ReturnType.FullName!="System.Nullable`1<System.Boolean>") throw new InvalidOperationException("Unexpected version status return type.");
        status.Body=new CilBody();
        status.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        status.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj,module.Import(typeof(bool?).GetConstructor(new[]{typeof(bool)}))));
        status.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var server=Method(module,"PCL.ModSecret","ServerSub");
        server.Body.SimplifyBranches();
        var il=server.Body.Instructions;
        var keys=il.Select((item,index)=>new{item,index}).Where(x=>x.item.OpCode==OpCodes.Ldstr && (string)x.item.Operand=="isUpdate").ToArray();
        if(keys.Length!=1) throw new InvalidOperationException("Unexpected update notice schema.");
        int key=keys[0].index,store=-1,settings=-1;
        for(int i=key+1;i<Math.Min(il.Count,key+15);i++)
            if(il[i].OpCode==OpCodes.Stloc || il[i].OpCode==OpCodes.Stloc_S){store=i;break;}
        for(int i=key+1;i<Math.Min(il.Count,key+45);i++)
            if(il[i].OpCode==OpCodes.Ldstr && (string)il[i].Operand=="SystemSystemUpdate"){settings=i;break;}
        if(store<0 || settings<0 || !(il[store].Operand is Local) || ((Local)il[store].Operand).Type.ElementType!=ElementType.Boolean)
            throw new InvalidOperationException("Update notice boolean was not identified.");
        var branch=il.Skip(settings+1).Take(10).FirstOrDefault(i=>i.OpCode==OpCodes.Bgt);
        if(branch==null || !(branch.Operand is Instruction)) throw new InvalidOperationException("Update notice continue branch missing.");
        var target=(Instruction)branch.Operand;
        if(!il.Skip(il.IndexOf(branch)+1).Take(8).Any(i=>i.OpCode==OpCodes.Brtrue && Object.ReferenceEquals(i.Operand,target)))
            throw new InvalidOperationException("Update notice continue target was not confirmed.");
        il.Insert(store+1,Instruction.Create(OpCodes.Ldloc,(Local)il[store].Operand));
        il.Insert(store+2,Instruction.Create(OpCodes.Brtrue,target));
        if(report) Console.WriteLine("LAUNCHER_UPDATE_POLICY 8 methods: manual check/download/restart/replace, version notices and update settings disabled; game/mod update code preserved.");
    }
}
