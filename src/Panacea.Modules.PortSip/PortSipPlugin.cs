using Panacea.Core;
using Panacea.Modularity.Telephone;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Panacea.Modules.PortSip
{
    public class PortSipPlugin : ITelephoneEnginePlugin
    {
        private readonly PanaceaServices _core;

        public PortSipPlugin(PanaceaServices core)
        {
            _core = core;
        }

        public Task BeginInit()
        {
            return Task.CompletedTask;
        }

        public TelephoneBase CreateInstance(ITelephoneAccount account)
        {
            return new PortSipTelephone(account, _core.Logger);
        }

        public void Dispose()
        {
            
        }

        public Task EndInit()
        {
            return Task.CompletedTask;
        }

        public Task Shutdown()
        {
            return Task.CompletedTask;
        }

        public bool SupportsType(string type)
        {
            return type == "sip";
        }
    }
}
