using System;
using System.Reflection;


public enum FieldType { Instance, Static }

public static class ReflectionHelper
{
    public static T GetField<T>(object objOrType, string fieldName, FieldType fieldType = FieldType.Instance)
    {
        Type type = fieldType == FieldType.Static ? (Type)objOrType : objOrType.GetType();
        BindingFlags flags = BindingFlags.NonPublic | (fieldType == FieldType.Static ? BindingFlags.Static : BindingFlags.Instance);
        FieldInfo field = type.GetField(fieldName, flags);
        object target = fieldType == FieldType.Static ? null : objOrType;
        return (T)field.GetValue(target);
    }

    public static void SetField<T>(object objOrType, string fieldName, T value, FieldType fieldType = FieldType.Instance)
    {
        Type type = fieldType == FieldType.Static ? (Type)objOrType : objOrType.GetType();
        BindingFlags flags = BindingFlags.NonPublic | (fieldType == FieldType.Static ? BindingFlags.Static : BindingFlags.Instance);
        FieldInfo field = type.GetField(fieldName, flags);
        object target = fieldType == FieldType.Static ? null : objOrType;
        field.SetValue(target, value);
    }
}