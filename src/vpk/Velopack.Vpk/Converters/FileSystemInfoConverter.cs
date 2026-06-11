
using System.ComponentModel;

namespace Velopack.Vpk.Converters;

public class FileSystemInfoConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }
    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
    {
        return destinationType == typeof(FileSystemInfo) || base.CanConvertTo(context, destinationType);
    }
    public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
    {
        if (value != null && value is string path) {
            return File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        }
        return base.ConvertFrom(context, culture, value);
    }
    public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
    {
        if (value is FileSystemInfo fsi && destinationType == typeof(string)) {
            return fsi.FullName;
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
