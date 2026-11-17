using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 4) throw new ArgumentException("original probe output audit");
if (Path.GetFullPath(args[0]) == Path.GetFullPath(args[2])) throw new Exception("Output must differ from input");
string expected = "16dedc48a2d8e261980c059aa6100cb5fdcfa985f6461b04809a8e8cef3416be";
if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant() != expected)
    throw new Exception("Wrong original runtime assembly");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
var read = new ReaderParameters { AssemblyResolver = resolver };
using var original = AssemblyDefinition.ReadAssembly(args[0], read);
using var helper = AssemblyDefinition.ReadAssembly(args[1], read);
using var originalCheck = AssemblyDefinition.ReadAssembly(args[0], read);
var module = original.MainModule;
if (original.Name.HasPublicKey || (module.Attributes & ModuleAttributes.StrongNameSigned) != 0)
    throw new Exception("Signed input is outside this probe builder");
const string typeName = "AstralParty.Android.Bootstrap";
// The game's compiler-data container already exists. Give helper array blobs
// their own container so new RVA fields cannot bind to absent original fields.
var helperData=helper.MainModule.Types.SingleOrDefault(t=>t.Name=="<PrivateImplementationDetails>");
if(helperData!=null){helperData.Namespace="AstralParty.Android";helperData.Name="CompilerData";}
var sources = helper.MainModule.GetTypes().Where(t=>t.Name!="<Module>").ToArray();
var typeMap = new Dictionary<TypeDefinition,TypeDefinition>();
var reused = new HashSet<TypeDefinition>();
var fieldMap = new Dictionary<FieldDefinition,FieldDefinition>();
var methodMap = new Dictionary<MethodDefinition,MethodDefinition>();
var genericMap = new Dictionary<GenericParameter,GenericParameter>();
var sourceTypes = sources.ToDictionary(t=>t.FullName);
var gameMethods = module.GetTypes().SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
var gameFields = module.GetTypes().SelectMany(t=>t.Fields).ToDictionary(f=>f.FullName);
var existingTypeRefs=module.GetTypeReferences().GroupBy(t=>t.FullName).ToDictionary(g=>g.Key,g=>g.First());
foreach(var src in sources) {
    if(!src.FullName.StartsWith("AstralParty.Android.",StringComparison.Ordinal) && !src.FullName.StartsWith("System.Runtime.CompilerServices.",StringComparison.Ordinal) && !src.FullName.StartsWith("Microsoft.CodeAnalysis.",StringComparison.Ordinal) && !src.FullName.StartsWith("<PrivateImplementationDetails>",StringComparison.Ordinal))
        throw new Exception("Unexpected helper type "+src.FullName);
    var existing=module.GetType(src.FullName);
    if(existing!=null) {
        if(src.FullName.StartsWith("AstralParty.Android.",StringComparison.Ordinal))throw new Exception("Mod already present "+src.FullName);
        typeMap[src]=existing;reused.Add(src);continue;
    }
    var dst=new TypeDefinition(src.Namespace,src.Name,src.Attributes,null) {PackingSize=src.PackingSize,ClassSize=src.ClassSize};
    typeMap[src]=dst;
    if(src.DeclaringType==null)module.Types.Add(dst);else typeMap[src.DeclaringType].NestedTypes.Add(dst);
    foreach(var gp in src.GenericParameters) {var n=new GenericParameter(gp.Name,dst){Attributes=gp.Attributes};dst.GenericParameters.Add(n);genericMap[gp]=n;}
}
TypeReference ImportType(TypeReference t) {
    if(t is GenericParameter gp)return genericMap.TryGetValue(gp,out var mapped)?mapped:gp;
    if(t is GenericInstanceType gi) {var n=new GenericInstanceType(ImportType(gi.ElementType)){IsValueType=gi.IsValueType};foreach(var arg in gi.GenericArguments)n.GenericArguments.Add(ImportType(arg));return n;}
    if(t is ArrayType array){var n=new ArrayType(ImportType(array.ElementType),array.Rank);for(int i=0;i<array.Dimensions.Count;i++)n.Dimensions[i]=array.Dimensions[i];return n;}
    if(t is ByReferenceType br)return new ByReferenceType(ImportType(br.ElementType));
    if(t is PointerType ptr)return new PointerType(ImportType(ptr.ElementType));
    if(t is PinnedType pin)return new PinnedType(ImportType(pin.ElementType));
    if(t is OptionalModifierType opt)return new OptionalModifierType(ImportType(opt.ModifierType),ImportType(opt.ElementType));
    if(t is RequiredModifierType req)return new RequiredModifierType(ImportType(req.ModifierType),ImportType(req.ElementType));
    if(t is SentinelType sen)return new SentinelType(ImportType(sen.ElementType));
    if(sourceTypes.TryGetValue(t.FullName,out var src))return typeMap[src];
    if(existingTypeRefs.TryGetValue(t.FullName,out var existingRef))return existingRef;
    // The shipped Unity framework files and game reference different legacy
    // signing identities for System. Bind newly used framework types to the
    // game's existing assembly scope, never introduce a second System identity.
    if(t.Scope is AssemblyNameReference scope && module.AssemblyReferences.FirstOrDefault(r=>r.Name==scope.Name) is { } knownScope)
    {
        var reference=new TypeReference(t.Namespace,t.Name,module,knownScope,t.IsValueType);
        if(t.DeclaringType!=null)reference.DeclaringType=ImportType(t.DeclaringType);
        foreach(var parameter in t.GenericParameters)reference.GenericParameters.Add(new GenericParameter(parameter.Name,reference){Attributes=parameter.Attributes});
        return reference;
    }
    return module.GetType(t.FullName)??module.ImportReference(t);
}
foreach(var src in sources.Where(t=>!reused.Contains(t))) {
    var dst=typeMap[src];dst.BaseType=src.BaseType==null?null:ImportType(src.BaseType);
    foreach(var i in src.Interfaces)dst.Interfaces.Add(new InterfaceImplementation(ImportType(i.InterfaceType)));
    foreach(var f in src.Fields) {
        var n=new FieldDefinition(f.Name,f.Attributes,ImportType(f.FieldType));
        if(f.HasConstant)n.Constant=f.Constant;
        if(f.HasLayoutInfo)n.Offset=f.Offset;
        if(f.InitialValue.Length>0)n.InitialValue=f.InitialValue.ToArray();
        dst.Fields.Add(n);fieldMap[f]=n;
    }
    foreach(var m in src.Methods) {
        var n=new MethodDefinition(m.Name,m.Attributes,module.TypeSystem.Void){ImplAttributes=m.ImplAttributes,CallingConvention=m.CallingConvention};
        dst.Methods.Add(n);methodMap[m]=n;
        foreach(var gp in m.GenericParameters){var x=new GenericParameter(gp.Name,n){Attributes=gp.Attributes};n.GenericParameters.Add(x);genericMap[gp]=x;}
    }
}
foreach(var pair in methodMap) {
    var src=pair.Key;var dst=pair.Value;dst.ReturnType=ImportType(src.ReturnType);
    foreach(var p in src.Parameters){var n=new ParameterDefinition(p.Name,p.Attributes,ImportType(p.ParameterType));if(p.HasConstant)n.Constant=p.Constant;dst.Parameters.Add(n);}
}
MethodReference ImportMethod(MethodReference m) {
    if(m is GenericInstanceMethod gi){var n=new GenericInstanceMethod(ImportMethod(gi.ElementMethod));foreach(var arg in gi.GenericArguments)n.GenericArguments.Add(ImportType(arg));return n;}
    if(m is MethodDefinition definition && methodMap.TryGetValue(definition,out var own))return own;
    if(sourceTypes.TryGetValue(m.DeclaringType.GetElementType().FullName,out var srcType) && !reused.Contains(srcType)) {
        if(m.DeclaringType is GenericInstanceType) {
            var constructedDefinition=m.Resolve();
            if(constructedDefinition==null||!methodMap.ContainsKey(constructedDefinition))throw new Exception("Unresolved constructed helper method "+m.FullName);
            var constructed=new MethodReference(m.Name,module.TypeSystem.Void,ImportType(m.DeclaringType)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};
            foreach(var gp in m.GenericParameters){var mapped=new GenericParameter(gp.Name,constructed){Attributes=gp.Attributes};constructed.GenericParameters.Add(mapped);genericMap[gp]=mapped;}
            constructed.ReturnType=ImportType(m.ReturnType);
            foreach(var parameter in m.Parameters)constructed.Parameters.Add(new ParameterDefinition(parameter.Name,parameter.Attributes,ImportType(parameter.ParameterType)));
            return constructed;
        }
        var found=srcType.Methods.SingleOrDefault(x=>x.FullName==m.FullName);
        if(found!=null)return methodMap[found];
        throw new Exception("Unresolved helper method "+m.FullName);
    }
    if(gameMethods.TryGetValue(m.FullName,out var game))return game;
    var result=new MethodReference(m.Name,module.TypeSystem.Void,ImportType(m.DeclaringType)){HasThis=m.HasThis,ExplicitThis=m.ExplicitThis,CallingConvention=m.CallingConvention};
    foreach(var gp in m.GenericParameters)result.GenericParameters.Add(new GenericParameter(gp.Name,result){Attributes=gp.Attributes});
    result.ReturnType=ImportType(m.ReturnType);
    foreach(var p in m.Parameters)result.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,ImportType(p.ParameterType)));
    return result;
}
FieldReference ImportField(FieldReference f) {
    if(f is FieldDefinition definition && fieldMap.TryGetValue(definition,out var own))return own;
    if(sourceTypes.TryGetValue(f.DeclaringType.GetElementType().FullName,out var srcType) && !reused.Contains(srcType)) {
        if(f.DeclaringType is GenericInstanceType) {
            var constructedDefinition=f.Resolve();if(constructedDefinition==null||!fieldMap.ContainsKey(constructedDefinition))throw new Exception("Unresolved constructed helper field "+f.FullName);
            return new FieldReference(f.Name,ImportType(f.FieldType),ImportType(f.DeclaringType));
        }
        var found=srcType.Fields.SingleOrDefault(x=>x.FullName==f.FullName);
        if(found!=null)return fieldMap[found];
        throw new Exception("Unresolved helper field "+f.FullName);
    }
    if(gameFields.TryGetValue(f.FullName,out var game))return game;
    return new FieldReference(f.Name,ImportType(f.FieldType),ImportType(f.DeclaringType));
}
void Attributes(ICustomAttributeProvider src,ICustomAttributeProvider dst) {
    CustomAttributeArgument Arg(CustomAttributeArgument a)=>new(ImportType(a.Type),a.Value is TypeReference t?ImportType(t):a.Value is CustomAttributeArgument[] arr?arr.Select(Arg).ToArray():a.Value);
    foreach(var a in src.CustomAttributes) {
        var n=new CustomAttribute(ImportMethod(a.Constructor));foreach(var x in a.ConstructorArguments)n.ConstructorArguments.Add(Arg(x));
        foreach(var x in a.Fields)n.Fields.Add(new CustomAttributeNamedArgument(x.Name,Arg(x.Argument)));
        foreach(var x in a.Properties)n.Properties.Add(new CustomAttributeNamedArgument(x.Name,Arg(x.Argument)));dst.CustomAttributes.Add(n);
    }
}
foreach(var pair in genericMap)foreach(var c in pair.Key.Constraints)pair.Value.Constraints.Add(new GenericParameterConstraint(ImportType(c.ConstraintType)));
foreach(var src in sources.Where(t=>!reused.Contains(t))) {
    var dst=typeMap[src];Attributes(src,dst);
    foreach(var p in src.Properties){var n=new PropertyDefinition(p.Name,p.Attributes,ImportType(p.PropertyType));if(p.GetMethod!=null)n.GetMethod=methodMap[p.GetMethod];if(p.SetMethod!=null)n.SetMethod=methodMap[p.SetMethod];if(p.HasConstant)n.Constant=p.Constant;foreach(var x in p.Parameters)n.Parameters.Add(new ParameterDefinition(x.Name,x.Attributes,ImportType(x.ParameterType)));dst.Properties.Add(n);Attributes(p,n);}
    foreach(var e in src.Events){var n=new EventDefinition(e.Name,e.Attributes,ImportType(e.EventType));if(e.AddMethod!=null)n.AddMethod=methodMap[e.AddMethod];if(e.RemoveMethod!=null)n.RemoveMethod=methodMap[e.RemoveMethod];if(e.InvokeMethod!=null)n.InvokeMethod=methodMap[e.InvokeMethod];dst.Events.Add(n);Attributes(e,n);}
    foreach(var f in src.Fields)Attributes(f,fieldMap[f]);
}
foreach(var pair in methodMap) {
    var src=pair.Key;var dst=pair.Value;Attributes(src,dst);Attributes(src.MethodReturnType,dst.MethodReturnType);
    for(int i=0;i<src.Parameters.Count;i++)Attributes(src.Parameters[i],dst.Parameters[i]);
    foreach(var ov in src.Overrides)dst.Overrides.Add(ImportMethod(ov));
    if(!src.HasBody)continue;
    dst.Body.InitLocals=src.Body.InitLocals;dst.Body.MaxStackSize=src.Body.MaxStackSize;
    foreach(var v in src.Body.Variables)dst.Body.Variables.Add(new VariableDefinition(ImportType(v.VariableType)));
    var insMap=new Dictionary<Instruction,Instruction>();
    foreach(var i in src.Body.Instructions){var n=Instruction.Create(OpCodes.Nop);n.OpCode=i.OpCode;insMap[i]=n;dst.Body.Instructions.Add(n);}
    foreach(var i in src.Body.Instructions)insMap[i].Operand=i.Operand switch {
        null=>null,Instruction j=>insMap[j],Instruction[] j=>j.Select(x=>insMap[x]).ToArray(),
        VariableDefinition v=>dst.Body.Variables[v.Index],ParameterDefinition a=>a.Index<0?dst.Body.ThisParameter:dst.Parameters[a.Index],
        FieldReference f=>ImportField(f),MethodReference m=>ImportMethod(m),TypeReference t=>ImportType(t),
        string or int or long or float or double or sbyte or byte=>i.Operand,
        _=>throw new Exception("Unsupported helper operand "+i.Operand.GetType())};
    Instruction? Map(Instruction? i)=>i==null?null:insMap[i];
    foreach(var e in src.Body.ExceptionHandlers)dst.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType){CatchType=e.CatchType==null?null:ImportType(e.CatchType),TryStart=Map(e.TryStart),TryEnd=Map(e.TryEnd),HandlerStart=Map(e.HandlerStart),HandlerEnd=Map(e.HandlerEnd),FilterStart=Map(e.FilterStart)});
}
var target=typeMap[sources.Single(t=>t.FullName==typeName)];
var hook = module.GetType("FairyGUI.StageEngine").Methods.Single(m => m.Name == "LateUpdate" && m.Parameters.Count == 0);
hook.Body.GetILProcessor().InsertBefore(hook.Body.Instructions[0], Instruction.Create(OpCodes.Call, target.Methods.Single(m => m.Name == "Tick")));
module.Mvid = Guid.NewGuid();
if (!module.AssemblyReferences.Select(a=>a.FullName).Order().SequenceEqual(originalCheck.MainModule.AssemblyReferences.Select(a=>a.FullName).Order()))
    throw new Exception("Probe introduced an assembly dependency: "+string.Join(";",module.AssemblyReferences.Select(a=>a.FullName).Except(originalCheck.MainModule.AssemblyReferences.Select(a=>a.FullName))));
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
original.Write(args[2]);

// Verify token-independent CIL, locals and exception regions of every original method.
using var output = AssemblyDefinition.ReadAssembly(args[2], read);
string Sig(TypeReference t) => t switch {
    GenericParameter g => (g.Type==GenericParameterType.Method?"!!":"!")+g.Position,
    GenericInstanceType g => (g.IsValueType?"valuetype:":"class:")+Sig(g.ElementType)+"<"+string.Join(",",g.GenericArguments.Select(Sig))+">",
    ArrayType a => Sig(a.ElementType)+"["+new string(',',a.Rank-1)+"]",
    ByReferenceType b => Sig(b.ElementType)+"&", PointerType p => Sig(p.ElementType)+"*",
    RequiredModifierType r => Sig(r.ElementType)+" modreq("+Sig(r.ModifierType)+")",
    OptionalModifierType r => Sig(r.ElementType)+" modopt("+Sig(r.ModifierType)+")", _=>t.FullName };
string MethodSig(MethodReference r) => r is GenericInstanceMethod g
    ? MethodSig(g.ElementMethod)+"<"+string.Join(",",g.GenericArguments.Select(Sig))+">"
    : Sig(r.ReturnType)+" "+Sig(r.DeclaringType)+"::"+r.Name+"`"+r.GenericParameters.Count+"("+string.Join(",",r.Parameters.Select(p=>Sig(p.ParameterType)))+")";
string Canonical(MethodDefinition m)
{
    if (!m.HasBody) return "no-body";
    var positions = m.Body.Instructions.Select((i,n) => (i,n)).ToDictionary(v => v.i,v => v.n);
    object? Operand(object? o) => o switch {
        Instruction i => positions[i], Instruction[] a => a.Select(i => positions[i]).ToArray(),
        MethodReference r => MethodSig(r), FieldReference f => Sig(f.FieldType)+" "+Sig(f.DeclaringType)+"::"+f.Name, TypeReference t => Sig(t), MemberReference r => r.FullName, VariableDefinition v => "local:" + v.Index,
        ParameterDefinition p => "param:" + p.Index,
        float f => "f32:" + Convert.ToHexString(BitConverter.GetBytes(f)),
        double d => "f64:" + Convert.ToHexString(BitConverter.GetBytes(d)), _ => o };
    return JsonSerializer.Serialize(new {
        m.Body.InitLocals,
        locals = m.Body.Variables.Select(v => Sig(v.VariableType)),
        instructions = m.Body.Instructions.Select(i => new { opcode = i.OpCode.Name, operand = Operand(i.Operand) }),
        exceptions = m.Body.ExceptionHandlers.Select(e => new { type=e.HandlerType.ToString(), caught=e.CatchType?.FullName,
            start=Operand(e.TryStart), end=Operand(e.TryEnd), handler=Operand(e.HandlerStart), handlerEnd=Operand(e.HandlerEnd), filter=Operand(e.FilterStart) }) });
}
var newMethods = output.MainModule.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
var changed = new List<string>(); var errors = new List<string>();
var originalMethods = originalCheck.MainModule.GetTypes().SelectMany(t => t.Methods).ToArray();
foreach (var m in originalMethods)
{
    if (!newMethods.TryGetValue(m.FullName, out var n)) { errors.Add("Missing "+m.FullName); continue; }
    if (m.Attributes != n.Attributes || m.ImplAttributes != n.ImplAttributes) errors.Add("Flags changed "+m.FullName);
    if (Canonical(m) != Canonical(n)) changed.Add(m.FullName);
}
var permitted=new HashSet<string>(){hook.FullName};
if (!permitted.SetEquals(changed) || errors.Count != 0)
    throw new Exception("Unexpected change set: " + JsonSerializer.Serialize(new { changed, errors }));
var reopenedHook = newMethods[hook.FullName];
if (reopenedHook.Body.Instructions[0].Operand is not MethodReference first || first.FullName != target.Methods.Single(m=>m.Name=="Tick").FullName)
    throw new Exception("Hook call missing");
var injectedCall = reopenedHook.Body.Instructions[0];
reopenedHook.Body.Instructions.RemoveAt(0);
if (Canonical(originalCheck.MainModule.GetType("FairyGUI.StageEngine").Methods.Single(m=>m.Name=="LateUpdate")) != Canonical(reopenedHook))
    throw new Exception("Original hook body changed beyond the injected call");
reopenedHook.Body.Instructions.Insert(0,injectedCall);
foreach(var method in output.MainModule.GetTypes().Where(t=>typeMap.Values.Any(added => added.FullName == t.FullName)).SelectMany(t=>t.Methods).Where(m=>m.HasBody))
    foreach(var instruction in method.Body.Instructions)
    {
        if(instruction.Operand is FieldReference field&&field.DeclaringType.Scope==output.MainModule&&field.Resolve()==null)throw new Exception("Missing injected field "+field.FullName);
        if(instruction.Operand is MethodReference member&&member.DeclaringType.Scope==output.MainModule&&member.Resolve()==null)throw new Exception("Missing injected method "+member.FullName);
    }
// Every injected helper method must also retain its token-independent body/signature.
foreach(var pair in methodMap) {
    var written=newMethods[pair.Value.FullName];
    if(Canonical(pair.Key)!=Canonical(written) || pair.Key.Attributes!=written.Attributes || pair.Key.ImplAttributes!=written.ImplAttributes)
    {File.WriteAllText(args[3]+".expected",Canonical(pair.Key));File.WriteAllText(args[3]+".actual",Canonical(written));throw new Exception("Injected helper method changed "+pair.Key.FullName);}
    if(!pair.Key.GenericParameters.Select(p=>string.Join(";",p.Constraints.Select(c=>Sig(c.ConstraintType)))).SequenceEqual(written.GenericParameters.Select(p=>string.Join(";",p.Constraints.Select(c=>Sig(c.ConstraintType))))))throw new Exception("Injected generic constraints changed "+pair.Key.FullName+" "+JsonSerializer.Serialize(new{source=pair.Key.GenericParameters.Select(p=>p.Constraints.Select(c=>Sig(c.ConstraintType)).ToArray()).ToArray(),memory=pair.Value.GenericParameters.Select(p=>p.Constraints.Select(c=>Sig(c.ConstraintType)).ToArray()).ToArray(),written=written.GenericParameters.Select(p=>p.Constraints.Select(c=>Sig(c.ConstraintType)).ToArray()).ToArray()}));
}
var audit = new {
    original_sha256=expected,
    patched_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[2]))).ToLowerInvariant(),
    original_methods_checked=originalMethods.Length, changed_original_methods=changed,
    added_types=sources.Where(t=>!reused.Contains(t)).Select(t=>t.FullName).ToArray(), added_method_count=methodMap.Count,
    original_mvid=originalCheck.MainModule.Mvid, patched_mvid=output.MainModule.Mvid,
    verification="Token-independent CIL, local types, exception regions and method flags compared; runtime not tested",
    original_assembly_references=originalCheck.MainModule.AssemblyReferences.Select(a=>a.FullName).Order(),
    patched_assembly_references=output.MainModule.AssemblyReferences.Select(a=>a.FullName).Order()
};
File.WriteAllText(args[3], JsonSerializer.Serialize(audit,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(audit));

