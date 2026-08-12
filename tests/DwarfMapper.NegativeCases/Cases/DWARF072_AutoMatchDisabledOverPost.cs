// SPDX-License-Identifier: GPL-2.0-only
// CASE: An explicit-only mapper refusing to auto-wire a same-named member
// WHY:  This is the over-posting guard, and its value depends entirely on the message. A same-named IsAdmin
//       arriving from an untrusted request model is exactly what must NOT be wired silently — but the reader
//       staring at two identically-named properties needs to be told that the refusal is the point, and how
//       to say "yes, deliberately".
// EXPECT: DWARF072, DWARF078
// EXPECT-MESSAGE DWARF072: 'IsAdmin'
// EXPECT-MESSAGE DWARF072: [MapProperty]
// EXPECT-MESSAGE DWARF072: [MapIgnore]
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class UserUpdateRequest
{
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }
}

public class User
{
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }
}

[DwarfMapper(AutoMatchMembers = false)]
public partial class M
{
    [MapProperty(nameof(UserUpdateRequest.Name), nameof(User.Name))]
    public partial User Map(UserUpdateRequest source);
}
