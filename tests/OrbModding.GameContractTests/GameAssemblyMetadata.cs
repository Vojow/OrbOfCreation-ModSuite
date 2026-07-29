using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OrbModding.GameContractTests;

internal sealed class GameAssemblyMetadata : IDisposable
{
    private readonly FileStream _stream;
    private readonly PEReader _peReader;
    private readonly MetadataTypeNameProvider _typeProvider;

    public GameAssemblyMetadata(string path)
    {
        _stream = File.OpenRead(path);
        _peReader = new PEReader(_stream);
        Reader = _peReader.GetMetadataReader();
        _typeProvider = new MetadataTypeNameProvider(Reader);
    }

    public MetadataReader Reader { get; }

    public bool HasType(string fullName)
    {
        return TryGetType(fullName, out _);
    }

    public string GetBaseType(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        return DecodeTypeHandle(definition.BaseType);
    }

    public TypeContract GetType(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        return new TypeContract(
            fullName,
            GetTypeVisibility(definition.Attributes),
            DecodeTypeHandle(definition.BaseType));
    }

    public string GetFieldType(string fullName, string fieldName)
    {
        return GetField(fullName, fieldName).FieldType;
    }

    public IReadOnlyDictionary<string, int> GetInt32EnumMembers(string fullName)
    {
        if (GetBaseType(fullName) != "System.Enum" || GetFieldType(fullName, "value__") != "System.Int32")
            throw new InvalidOperationException($"Type {fullName} is not an Int32-backed enum.");
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var members = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil) continue;
            var constant = Reader.GetConstant(constantHandle);
            if (constant.TypeCode != ConstantTypeCode.Int32)
                throw new InvalidOperationException($"Enum {fullName} does not use Int32 constants.");
            members.Add(Reader.GetString(field.Name), Reader.GetBlobReader(constant.Value).ReadInt32());
        }
        return members;
    }

    /// <summary>
    /// Every field a type declares itself, in metadata order. Inherited fields are not included,
    /// which matches how a collector enumerates a category: what the type carries, not what its base
    /// contributes.
    /// </summary>
    public IReadOnlyList<FieldContract> GetFields(string fullName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var fields = new List<FieldContract>();
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            fields.Add(new FieldContract(
                Reader.GetString(field.Name),
                GetFieldVisibility(field.Attributes),
                (field.Attributes & FieldAttributes.Static) != 0,
                field.DecodeSignature(_typeProvider, null)));
        }

        return fields;
    }

    public FieldContract GetField(string fullName, string fieldName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            if (Reader.GetString(field.Name) == fieldName)
            {
                return new FieldContract(
                    fieldName,
                    GetFieldVisibility(field.Attributes),
                    (field.Attributes & FieldAttributes.Static) != 0,
                    field.DecodeSignature(_typeProvider, null));
            }
        }

        throw new InvalidOperationException($"Field {fullName}.{fieldName} was not found.");
    }

    public IReadOnlyList<MethodContract> GetMethods(string fullName, string methodName)
    {
        var definition = Reader.GetTypeDefinition(RequireType(fullName));
        var methods = new List<MethodContract>();
        foreach (var methodHandle in definition.GetMethods())
        {
            var method = Reader.GetMethodDefinition(methodHandle);
            var name = Reader.GetString(method.Name);
            if (name != methodName)
            {
                continue;
            }

            var signature = method.DecodeSignature(_typeProvider, null);
            methods.Add(new MethodContract(
                name,
                GetMethodVisibility(method.Attributes),
                (method.Attributes & MethodAttributes.Static) != 0,
                signature.ReturnType,
                signature.ParameterTypes.ToArray()));
        }

        return methods;
    }

    private static string GetTypeVisibility(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
            TypeAttributes.NestedFamily => "family",
            TypeAttributes.NestedAssembly => "assembly",
            TypeAttributes.NestedFamORAssem => "family-or-assembly",
            TypeAttributes.NestedFamANDAssem => "family-and-assembly",
            _ => "private",
        };

    private static string GetMethodVisibility(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Family => "family",
            MethodAttributes.Assembly => "assembly",
            MethodAttributes.FamORAssem => "family-or-assembly",
            MethodAttributes.FamANDAssem => "family-and-assembly",
            _ => "private",
        };

    private static string GetFieldVisibility(FieldAttributes attributes) =>
        (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => "public",
            FieldAttributes.Family => "family",
            FieldAttributes.Assembly => "assembly",
            FieldAttributes.FamORAssem => "family-or-assembly",
            FieldAttributes.FamANDAssem => "family-and-assembly",
            _ => "private",
        };

    public void Dispose()
    {
        _peReader.Dispose();
        _stream.Dispose();
    }

    private TypeDefinitionHandle RequireType(string fullName)
    {
        if (TryGetType(fullName, out var handle))
        {
            return handle;
        }

        throw new InvalidOperationException($"Type {fullName} was not found.");
    }

    private bool TryGetType(string fullName, out TypeDefinitionHandle handle)
    {
        foreach (var candidate in Reader.TypeDefinitions)
        {
            if (GetFullTypeName(candidate) == fullName)
            {
                handle = candidate;
                return true;
            }
        }

        handle = default;
        return false;
    }

    private string DecodeTypeHandle(EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => _typeProvider.GetTypeFromDefinition(Reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => _typeProvider.GetTypeFromReference(Reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => _typeProvider.GetTypeFromSpecification(Reader, null, (TypeSpecificationHandle)handle, 0),
            _ => handle.IsNil ? string.Empty : handle.Kind.ToString(),
        };
    }

    private string GetFullTypeName(TypeDefinitionHandle handle)
    {
        var definition = Reader.GetTypeDefinition(handle);
        var name = Reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return GetFullTypeName(declaring) + "+" + name;
        }

        var typeNamespace = Reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }
}

internal sealed record MethodContract(
    string Name,
    string Visibility,
    bool IsStatic,
    string ReturnType,
    IReadOnlyList<string> ParameterTypes);

internal sealed record FieldContract(
    string Name,
    string Visibility,
    bool IsStatic,
    string FieldType);

internal sealed record TypeContract(
    string Name,
    string Visibility,
    string BaseType);

internal sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;

    public MetadataTypeNameProvider(MetadataReader reader)
    {
        _reader = reader;
    }

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Void => "System.Void",
        _ => typeCode.ToString(),
    };

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return GetTypeFromDefinition(reader, declaring, rawTypeKind) + "+" + name;
        }

        var typeNamespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) + "+" + name;
        }

        var typeNamespace = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
