#region License

/*
 Copyright 2014 - 2015 Nikita Bernthaler
 AIHeroClientExtensions.cs is part of SFXLibrary.

 SFXLibrary is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 SFXLibrary is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with SFXLibrary. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion License

#region

using EloBuddy;
using LeagueSharp.Common;

#endregion

namespace SFXWard.Library.Extensions.LeagueSharp
{
    // ReSharper disable once InconsistentNaming
    public static class AIHeroClientExtensions
    {
        public static bool HasItem(this AIHeroClient hero, int id)
        {
            return hero != null && Items.HasItem(id, hero);
        }

        public static bool HasItem(this AIHeroClient hero, ItemId item)
        {
            return hero != null && Items.HasItem((int) item, hero);
        }
    }
}