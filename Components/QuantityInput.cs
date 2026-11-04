using System.Globalization;
using Microsoft.AspNetCore.Components.Forms;

namespace CharleyCompany.Dashboard.Web.Components;

// Keep decimal parsing and validation while removing only insignificant display zeros.
public sealed class QuantityInput : InputNumber<decimal>
{
    protected override string? FormatValueAsString(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
