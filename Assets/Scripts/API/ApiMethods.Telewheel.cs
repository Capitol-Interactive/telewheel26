// Copyright 2026 Capitol Interactive LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace TiltBrush
{
    public static partial class ApiMethods
    {
        [ApiEndpoint(
            "telewheel.state",
            "Reports whether the Telewheel game has started and how the self-test went."
        )]
        public static string TelewheelState()
        {
            global::Telewheel.TwDirector director = global::Telewheel.TwDirector.Instance;
            return director == null ? "telewheel not running" : director.StateText();
        }

        [ApiEndpoint(
            "telewheel.selftest",
            "Runs the Telewheel self-test. Read the result with telewheel.selftest.report."
        )]
        public static string TelewheelSelfTest()
        {
            global::Telewheel.TwDirector director = global::Telewheel.TwDirector.Instance;
            if (director == null || !director.Started)
            {
                return "telewheel not running";
            }
            director.RunSelfTest();
            return "started";
        }

        [ApiEndpoint(
            "telewheel.selftest.report",
            "Returns the result of the last Telewheel self-test, one line per check."
        )]
        public static string TelewheelSelfTestReport()
        {
            global::Telewheel.TwDirector director = global::Telewheel.TwDirector.Instance;
            return director == null ? "telewheel not running" : director.SelfTest.ToText();
        }
    }
}
