using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace GiganticEmu.Shared.Backend
{
    public class User : IdentityUser<Guid>
    {
        public int Rank { get; set; } = 69;

        public string SavedLoadouts { get; set; } = "{}";

        public string ProfileSettings { get; set; } = "{}";

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int MotigaId { get; set; }

        public string? AuthToken { get; set; } = null;

        public DateTimeOffset? AuthTokenExpires { get; set; } = null;

        public DateTimeOffset LastOnline { get; set; } = DateTimeOffset.MinValue;

        public Guid? SessionId { get; set; } = null;

        public int SessionVersion { get; set; } = 0;

        public bool IsSessionHost { get; set; } = false;

        public string MemberSettings { get; set; } = "{}";

        public string JoinState { get; set; } = "open";

        public string SessionSettings { get; set; } = "{}";

        public string SessionConfiguration { get; set; } = "{}";

        public string? SalsaSCK { get; set; } = null;

        public bool InQueue { get; set; } = false;

        public void ClearSession(bool clearMemberSettings = true)
        {
            InQueue = false;
            SessionId = null;
            SessionVersion = 0;
            SessionConfiguration = "{}";
            SessionSettings = "{}";
            JoinState = "open";
            IsSessionHost = false;
            if (clearMemberSettings)
            {
                MemberSettings = "{}";
            }
        }
    }
}
