using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static class UnitDefinitionCreator
{
    public static void Modify(UnitDefinition unitDefinition, Action<UnitDefinition> action)
    {
        action.Invoke(unitDefinition);
    }
}