// JSON and number text exactly as the Python reference writes and reads them. The value tree and the writer are the other importers'
// (JObj = an insertion-ordered dict, List<object> / arrays = lists, string, long, double, bool, null; UxJson: float repr, key order,
// separators, ensure_ascii); this file names the forms the Unleashed converter uses.
using System;
using System.Collections.Generic;
using System.Text;
using Reflex.Port.Content.AliveImport;
using Reflex.Port.Content.UntamedImport;

namespace Reflex.Port.Content.UnleashedImport
{
    public static class UnJson
    {
        /// json.dumps(v, indent=indent): keys in insertion order, ensure_ascii, "," / ": " with an indent, ", " / ": " without
        /// (indent < 0 = indent=None). A C# float is written as float(np.float32), a double as repr(float).
        public static string Dumps(object v, int indent = -1) => UxJson.Dumps(v, indent, false, true, true);

        /// json.dumps(v, indent=indent).encode().
        public static byte[] DumpsBytes(object v, int indent = -1) => Encoding.UTF8.GetBytes(Dumps(v, indent));

        /// json.loads(text): objects as JObj (key order kept), lists as List<object>, integers as long, other numbers as the exactly
        /// rounded double.
        public static object Loads(string text) => UxJson.Parse(text);

        /// float(text) for the decimal forms the source files hold ("12", "-3.5", " 1e-3 "): surrounding whitespace stripped, the nearest
        /// double by exact arithmetic (the same on every runtime). Python's "inf" / "nan" / digit underscores are not accepted:
        /// UnValueError, as for any other text Python's float() refuses.
        public static double Float(string text)
        {
            if (text == null) throw new UnValueError("float() of nothing");
            string s = UnIni.Strip(text);
            try { return UxJson.ParseDouble(s); }
            catch (Exception) { throw new UnValueError("could not convert string to float: '" + text + "'"); }
        }

        /// round(x, ndigits) as Python rounds a float (correctly rounded decimal, ties to even on the exact value).
        public static double Round(double x, int ndigits) => UxNp.PyRound(x, ndigits);

        public static JObj Obj(object v) => v as JObj ?? throw new UnValueError("JSON: object expected");
        public static List<object> Arr(object v) => v as List<object> ?? throw new UnValueError("JSON: list expected");
    }
}
