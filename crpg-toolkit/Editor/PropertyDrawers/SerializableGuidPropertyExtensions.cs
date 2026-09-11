using CRPG.Core;
using UnityEditor;

namespace CRPG.Editor.PropertyDrawers
{
    /// <summary>Reads/writes a SerializableGuid through its "low"/"high" backing fields via SerializedProperty
    /// -- shared by the drawer and by any Editor tool (the Dialogue graph editor) that needs to get or set a
    /// SerializableGuid-typed field without repeating the FindPropertyRelative pair every time.</summary>
    public static class SerializableGuidPropertyExtensions
    {
        public static SerializableGuid GetValue(SerializedProperty guidProperty) =>
            GetValue(guidProperty.FindPropertyRelative("low"), guidProperty.FindPropertyRelative("high"));

        public static SerializableGuid GetValue(SerializedProperty lowProperty, SerializedProperty highProperty)
        {
            var bytes = new byte[16];
            System.BitConverter.GetBytes(lowProperty.longValue).CopyTo(bytes, 0);
            System.BitConverter.GetBytes(highProperty.longValue).CopyTo(bytes, 8);
            return SerializableGuid.FromGuid(new System.Guid(bytes));
        }

        public static void SetValue(SerializedProperty guidProperty, SerializableGuid value) =>
            SetValue(guidProperty.FindPropertyRelative("low"), guidProperty.FindPropertyRelative("high"), value);

        public static void SetValue(SerializedProperty lowProperty, SerializedProperty highProperty, SerializableGuid value)
        {
            var bytes = value.ToGuid().ToByteArray();
            lowProperty.longValue = System.BitConverter.ToInt64(bytes, 0);
            highProperty.longValue = System.BitConverter.ToInt64(bytes, 8);
        }

        public static string ReadDisplayString(SerializedProperty lowProperty, SerializedProperty highProperty)
        {
            var value = GetValue(lowProperty, highProperty);
            return value.IsValid ? value.ToString() : "(none)";
        }
    }
}
