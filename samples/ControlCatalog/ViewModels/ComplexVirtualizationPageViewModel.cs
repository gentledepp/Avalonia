using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MiniMvvm;

namespace ControlCatalog.ViewModels
{
    public class ComplexVirtualizationPageViewModel : ViewModelBase
    {
        private bool _enableVirtualization;

        public ComplexVirtualizationPageViewModel()
        {
            // Create a mixed collection of different item types
            var items = new List<object>();
            var random = new Random(42); // Fixed seed for consistent data

            for (int i = 0; i < 5000; i++)
            {
                // Randomly distribute items across 4 types (weighted distribution)
                // This creates a more realistic scenario where types aren't evenly distributed
                var type = random.Next(100) switch
                {
                    < 30 => 0,  // 30% PersonItem
                    < 55 => 1,  // 25% TaskItem
                    < 80 => 2,  // 25% ProductItem
                    _ => 3      // 20% PhotoItem
                };
                switch (type)
                {
                    case 0:
                        var bioLength = random.Next(0, 5); // 0-4 sentences
                        var bio = bioLength > 0 ? string.Join(" ", Enumerable.Range(0, bioLength).Select(_ => SampleText[random.Next(SampleText.Length)])) : "";
                        var hasSkills = random.Next(2) == 0;
                        var skillCount = hasSkills ? random.Next(2, 7) : 0;
                        var skills = new List<string>();
                        if (hasSkills)
                        {
                            for (int j = 0; j < skillCount; j++)
                            {
                                skills.Add(Skills[random.Next(Skills.Length)]);
                            }
                        }
                        items.Add(new PersonItem
                        {
                            Id = i,
                            Name = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}",
                            Email = $"person{i}@{Domains[random.Next(Domains.Length)]}",
                            Department = Departments[random.Next(Departments.Length)],
                            Bio = bio,
                            PhoneNumber = $"+1 {random.Next(200, 999)}-{random.Next(100, 999)}-{random.Next(1000, 9999)}",
                            YearsExperience = random.Next(0, 30),
                            Skills = skills,
                            IsActive = random.Next(2) == 0,
                            LastActivity = DateTime.Now.AddDays(-random.Next(0, 90))
                        });
                        break;
                    case 1:
                        var descLength = random.Next(1, 6); // 1-5 sentences
                        var description = string.Join(" ", Enumerable.Range(0, descLength).Select(_ => SampleText[random.Next(SampleText.Length)]));
                        var hasSubtasks = random.Next(3) == 0; // 33% chance
                        var subtaskCount = hasSubtasks ? random.Next(2, 6) : 0;
                        var subtasks = new List<string>();
                        if (hasSubtasks)
                        {
                            for (int j = 0; j < subtaskCount; j++)
                            {
                                subtasks.Add($"{TaskActions[random.Next(TaskActions.Length)]} {TaskObjects[random.Next(TaskObjects.Length)]}");
                            }
                        }
                        items.Add(new TaskItem
                        {
                            Id = i,
                            Title = $"{TaskActions[random.Next(TaskActions.Length)]} {TaskObjects[random.Next(TaskObjects.Length)]}",
                            Description = description,
                            IsCompleted = random.Next(2) == 0,
                            Priority = (Priority)random.Next(3),
                            DueDate = DateTime.Now.AddDays(random.Next(-10, 30)),
                            Assignee = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}",
                            Subtasks = subtasks,
                            ProgressPercentage = random.Next(0, 101)
                        });
                        break;
                    case 2:
                        var tagCount = random.Next(1, 10); // 1-9 tags
                        var tags = new List<string>();
                        for (int j = 0; j < tagCount; j++)
                        {
                            var tag = AllTags[random.Next(AllTags.Length)];
                            if (!tags.Contains(tag)) tags.Add(tag);
                        }
                        var hasDescription = random.Next(2) == 0; // 50% chance
                        var productDesc = hasDescription ? string.Join(" ", Enumerable.Range(0, random.Next(1, 4)).Select(_ => SampleText[random.Next(SampleText.Length)])) : "";
                        items.Add(new ProductItem
                        {
                            Id = i,
                            ProductName = $"{Adjectives[random.Next(Adjectives.Length)]} {ProductTypes[random.Next(ProductTypes.Length)]}",
                            Price = random.Next(10, 1000),
                            Tags = tags,
                            InStock = random.Next(2) == 0,
                            Rating = random.Next(1, 6),
                            ReviewCount = random.Next(0, 5000),
                            Description = productDesc,
                            Category = ProductCategories[random.Next(ProductCategories.Length)],
                            Discount = random.Next(4) == 0 ? random.Next(5, 50) : 0 // 25% chance of discount
                        });
                        break;
                    case 3:
                        var captionLength = random.Next(0, 5); // 0-4 sentences
                        var caption = captionLength > 0 ? string.Join(" ", Enumerable.Range(0, captionLength).Select(_ => SampleText[random.Next(SampleText.Length)])) : "";
                        var hasComments = random.Next(3) == 0; // 33% chance
                        var commentCount = hasComments ? random.Next(1, 5) : 0;
                        var comments = new List<string>();
                        if (hasComments)
                        {
                            for (int j = 0; j < commentCount; j++)
                            {
                                comments.Add($"{FirstNames[random.Next(FirstNames.Length)]}: {SampleText[random.Next(SampleText.Length)]}");
                            }
                        }
                        items.Add(new PhotoItem
                        {
                            Id = i,
                            Title = $"{PhotoAdjectives[random.Next(PhotoAdjectives.Length)]} {PhotoSubjects[random.Next(PhotoSubjects.Length)]}",
                            Location = Locations[random.Next(Locations.Length)],
                            ImageUrl = $"avares://ControlCatalog/Assets/Icons/avalonia-32.png",
                            Caption = caption,
                            Likes = random.Next(0, 10000),
                            DateTaken = DateTime.Now.AddDays(-random.Next(0, 1000)),
                            Comments = comments,
                            CameraModel = random.Next(2) == 0 ? CameraModels[random.Next(CameraModels.Length)] : "",
                            IsPublic = random.Next(2) == 0
                        });
                        break;
                }
            }

            Items = new ObservableCollection<object>(items);
        }

        public ObservableCollection<object> Items { get; }

        public bool EnableVirtualization
        {
            get => _enableVirtualization;
            set => this.RaiseAndSetIfChanged(ref _enableVirtualization, value);
        }

        private static readonly string[] Departments = { "Engineering", "Sales", "Marketing", "HR", "Finance", "Operations", "R&D", "Customer Support" };
        private static readonly string[] Locations = { "New York", "London", "Tokyo", "Paris", "Sydney", "Berlin", "Singapore", "Toronto", "Dubai", "Mumbai" };
        private static readonly string[] AllTags = { "Featured", "New", "Sale", "Popular", "Limited", "Premium", "Eco-Friendly", "Best Seller", "Trending", "Exclusive", "Organic", "Handmade" };
        private static readonly string[] FirstNames = { "Alex", "Jordan", "Morgan", "Taylor", "Casey", "Riley", "Avery", "Quinn", "Sage", "Rowan" };
        private static readonly string[] LastNames = { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez" };
        private static readonly string[] Domains = { "example.com", "company.com", "business.org", "enterprise.net" };
        private static readonly string[] TaskActions = { "Review", "Update", "Complete", "Investigate", "Fix", "Implement", "Design", "Test", "Deploy", "Analyze" };
        private static readonly string[] TaskObjects = { "Documentation", "Bug Report", "Feature Request", "Security Issue", "Performance", "UI/UX", "Database", "API", "Integration", "Configuration" };
        private static readonly string[] Adjectives = { "Premium", "Deluxe", "Professional", "Standard", "Economy", "Elite", "Advanced", "Basic", "Ultimate", "Classic" };
        private static readonly string[] ProductTypes = { "Widget", "Gadget", "Tool", "Device", "Kit", "System", "Module", "Component", "Package", "Bundle" };
        private static readonly string[] PhotoAdjectives = { "Stunning", "Beautiful", "Majestic", "Serene", "Vibrant", "Peaceful", "Dramatic", "Colorful", "Minimalist", "Abstract" };
        private static readonly string[] PhotoSubjects = { "Sunset", "Landscape", "Portrait", "Architecture", "Wildlife", "Street Scene", "Nature", "Cityscape", "Ocean View", "Mountain" };
        private static readonly string[] SampleText =
        {
            "Lorem ipsum dolor sit amet consectetur adipiscing elit.",
            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
            "Ut enim ad minim veniam quis nostrud exercitation ullamco.",
            "Duis aute irure dolor in reprehenderit in voluptate velit.",
            "Excepteur sint occaecat cupidatat non proident sunt in culpa."
        };
        private static readonly string[] Skills = { "C#", "Python", "JavaScript", "React", "Angular", "Vue", "Node.js", "Docker", "Kubernetes", "AWS", "Azure", "SQL", "MongoDB", "Git" };
        private static readonly string[] ProductCategories = { "Electronics", "Clothing", "Books", "Home & Garden", "Sports", "Toys", "Food & Beverage" };
        private static readonly string[] CameraModels = { "Canon EOS R5", "Nikon Z9", "Sony A7R V", "Fujifilm X-T5", "iPhone 14 Pro" };
    }

    public enum Priority
    {
        Low,
        Medium,
        High
    }

    public class PersonItem : ViewModelBase
    {
        private string _name = string.Empty;

        public int Id { get; set; }

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public int YearsExperience { get; set; }
        public List<string> Skills { get; set; } = new();
        public bool IsActive { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class TaskItem : ViewModelBase
    {
        private bool _isCompleted;

        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsCompleted
        {
            get => _isCompleted;
            set => this.RaiseAndSetIfChanged(ref _isCompleted, value);
        }

        public Priority Priority { get; set; }
        public DateTime DueDate { get; set; }
        public string Assignee { get; set; } = string.Empty;
        public List<string> Subtasks { get; set; } = new();
        public int ProgressPercentage { get; set; }
    }

    public class ProductItem
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public List<string> Tags { get; set; } = new();
        public bool InStock { get; set; }
        public int Rating { get; set; }
        public int ReviewCount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Discount { get; set; }
    }

    public class PhotoItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public int Likes { get; set; }
        public DateTime DateTaken { get; set; }
        public List<string> Comments { get; set; } = new();
        public string CameraModel { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
    }
}
