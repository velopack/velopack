using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console.Rendering;

namespace Velopack.Vpk.Logging;

public class RenderableCollection : IRenderable
{
    private readonly IEnumerable<IRenderable> _items;

    public RenderableCollection(IEnumerable<IRenderable> items)
    {
        _items = items;
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        return default(Measurement);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        foreach (Segment item in _items.SelectMany((IRenderable i) => i.Render(options, maxWidth))) {
            yield return item;
        }
    }
}
