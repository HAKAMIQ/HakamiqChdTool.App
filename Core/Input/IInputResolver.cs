using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Core.Input;

public interface IInputResolver
{
    IEnumerable<string> Resolve(string path);

    IEnumerable<string> Resolve(string path, SearchOption searchOption);
}