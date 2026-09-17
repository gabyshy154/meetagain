using System.ComponentModel.DataAnnotations;

namespace MeetAgain.Server.Models
{
    public class CreateMeetupModel
    {
        [Required]
        public string Title { get; set; } = "";

        [Required]
        public string Description { get; set; } = "";
        
        [Required]
        public string Location { get; set; } = string.Empty;

        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        public TimeOnly Time { get; set; } = new TimeOnly(12, 0);
    }
}
