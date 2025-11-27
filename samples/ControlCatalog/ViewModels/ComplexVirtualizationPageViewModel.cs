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
                // Distribute items across 4 types
                var type = i % 4;
                switch (type)
                {
                    case 0:
                        items.Add(new PersonItem
                        {
                            Id = i,
                            Name = $"Person {i}",
                            Email = $"person{i}@example.com",
                            Department = Departments[random.Next(Departments.Length)]
                        });
                        break;
                    case 1:
                        items.Add(new TaskItem
                        {
                            Id = i,
                            Title = $"Task {i}",
                            Description = $"Complete important task number {i}",
                            IsCompleted = random.Next(2) == 0,
                            Priority = (Priority)random.Next(3)
                        });
                        break;
                    case 2:
                        var tagCount = random.Next(2, 6);
                        var tags = new List<string>();
                        for (int j = 0; j < tagCount; j++)
                        {
                            tags.Add(AllTags[random.Next(AllTags.Length)]);
                        }
                        items.Add(new ProductItem
                        {
                            Id = i,
                            ProductName = $"Product {i}",
                            Price = random.Next(10, 1000),
                            Tags = tags
                        });
                        break;
                    case 3:
                        items.Add(new PhotoItem
                        {
                            Id = i,
                            Title = $"Photo {i}",
                            Location = Locations[random.Next(Locations.Length)],
                            ImageUrl = $"avares://ControlCatalog/Assets/Icons/avalonia-32.png"
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

        private static readonly string[] Departments = { "Engineering", "Sales", "Marketing", "HR", "Finance" };
        private static readonly string[] Locations = { "New York", "London", "Tokyo", "Paris", "Sydney" };
        private static readonly string[] AllTags = { "Featured", "New", "Sale", "Popular", "Limited", "Premium", "Eco-Friendly", "Best Seller" };
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
    }

    public class ProductItem
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class PhotoItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }
}
