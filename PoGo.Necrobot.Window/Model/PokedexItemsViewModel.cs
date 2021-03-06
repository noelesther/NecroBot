﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using POGOProtos.Inventory;
using POGOProtos.Enums;
using System.Windows.Media.Animation;

namespace PoGo.Necrobot.Window.Model
{
    public class PokedexItemsViewModel : ViewModelBase
    {
        public PokedexItemsViewModel()
        {
            Entries = new ObservableCollection<PokeDexEntryViewModel>();
            DefaultValues();
        }

        public PokedexItemsViewModel(List<InventoryItem> list)
        {
            Entries = new ObservableCollection<PokeDexEntryViewModel>();

            DefaultValues();
            UpdateWith(list);
        }

        private void DefaultValues()
        {
            foreach (PokemonId e in Enum.GetValues(typeof(PokemonId)))
            {
                if (e == PokemonId.Missingno || (int)e > 251) continue;
                Entries.Add(new PokeDexEntryViewModel()
                {
                    PokemonId = e,
                    Caught = 0,
                    Seen = 0
                });
            }
        }

        public ObservableCollection<PokeDexEntryViewModel> Entries { get; set; }

        internal void UpdateWith(List<InventoryItem> list)
        {
            foreach (var item in list)
            {
                var entry = item.InventoryItemData.PokedexEntry;

                var x = this.Entries.FirstOrDefault(p => p.PokemonId == entry.PokemonId);
                x.Caught = entry.TimesCaptured;
                x.Seen = entry.TimesEncountered;
                x.Opacity = (entry.TimesCaptured > 0 ? 1.0:0.0);

                if(entry.TimesCaptured > 0)
                {
                    x.TimelineDuration = "24:00:00";
                }
                else
                {
                    x.TimelineDuration = "0:0:4";
                }
                x.RaisePropertyChanged("Caught");
                x.RaisePropertyChanged("Seen");
                x.RaisePropertyChanged("Opacity");
                x.RaisePropertyChanged("TimelineDuration");
            }
        }
    }
}
