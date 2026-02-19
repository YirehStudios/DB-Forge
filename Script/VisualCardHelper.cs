using Godot;
using System;

namespace DbfForge.UI.Components
{
    public partial class VisualCardHelper : Node
    {
        [ExportCategory("UI References")]
        [Export] public Control RootControl; 
        [Export] public Label LblStatus;
        [Export] public TextureRect IconStatus;
        
        [ExportCategory("Status Icons")]
        [Export] public Texture2D TexWait;    // State 0
        [Export] public Texture2D TexProcess; // State 1
        [Export] public Texture2D TexSuccess; // State 2
        [Export] public Texture2D TexError;   // State 3
        [Export] public Texture2D TexWarning; // State 4 and 5
        private Tween _activeTween;

        // Initializes node references and base opacity
        public override void _Ready()
        {
            if (RootControl == null) RootControl = GetParent() as Control;
            if (RootControl != null) RootControl.Modulate = new Color(1, 1, 1, 0); 
        }

        // Executes entry interpolation (fade-in) with configurable delay
        public void AnimateEntry(float delay)
        {
            if (RootControl == null) return;
            var tween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenInterval(delay);
            tween.TweenProperty(RootControl, "modulate:a", 1.0f, 0.6f);
        }

        // Updates card visualization based on integer state index
        public void UpdateVisualState(int state)
        {
            // Local variable definition for styling
            Color baseColor = new Color("71717a"); 
            string statusText = "READY";
            
            // Initialize texture with default to avoid null refs
            Texture2D finalIcon = TexWait; 

            // State Machine for color, text, and icon
            switch (state)
            {
                case 0: // PREPARED/READY
                    baseColor = new Color("06b6d4");  
                    statusText = "READY";
                    finalIcon = TexWait;
                    break;
                case 1: // PROCESSING
                    baseColor = new Color("3b82f6");  
                    statusText = "PROCESSING";
                    finalIcon = TexProcess;
                    break;
                case 2: // SUCCESS
                    baseColor = new Color("10b981");  
                    statusText = "SUCCESS";
                    finalIcon = TexSuccess;
                    break;
                case 3: // ERROR
                    baseColor = new Color("ef4444");  
                    statusText = "ERROR";
                    finalIcon = TexError;
                    break;
                case 4: // WARNING
                    baseColor = new Color("eab308");  
                    statusText = "WARNING";        
                    // Null coalescing: use Error icon if Warning is null
                    finalIcon = TexWarning ?? TexError; 
                    break;
                case 5: // RISK
                    baseColor = new Color("f97316");  
                    statusText = "RISK";             
                    finalIcon = TexWarning ?? TexError;
                    break;
            }

            // Generate derived colors for background and border
            Color bgTint = new Color(baseColor.R, baseColor.G, baseColor.B, 0.15f); 
            Color solidColor = new Color(baseColor.R, baseColor.G, baseColor.B, 1.0f); 

            // Kill previous animation to prevent overlapping states
            if (_activeTween != null && _activeTween.IsValid())
            {
                _activeTween.Kill();
            }

            // Parallel animation configuration
            _activeTween = CreateTween().SetParallel(true);

            // Apply text and color animation to Label
            if (LblStatus != null) {
                LblStatus.Text = statusText;
                _activeTween.TweenProperty(LblStatus, "modulate", solidColor, 0.2f);
            }

            // Apply texture and color animation to Icon
            if (IconStatus != null) {
                IconStatus.Texture = finalIcon;
                _activeTween.TweenProperty(IconStatus, "self_modulate", solidColor, 0.2f);
            }

            // Dynamic modification of the parent container's StyleBox (Pill shape)
            var pillContainer = LblStatus?.GetParent()?.GetParent() as PanelContainer;
            if (pillContainer != null) {
                var style = pillContainer.GetThemeStylebox("panel") as StyleBoxFlat;
                
                // Duplicate style resource to maintain immutability across instances
                // Optimization: Only duplicate if strict isolation is needed, otherwise modifying a unique instance is preferred
                if (style != null) {
                    var newStyle = style.Duplicate() as StyleBoxFlat;
                    newStyle.BgColor = bgTint;           
                    newStyle.BorderColor = solidColor;   
                    newStyle.BorderWidthBottom = 2; 
                    newStyle.BorderWidthLeft = 1;
                    newStyle.BorderWidthRight = 1;
                    newStyle.BorderWidthTop = 1;
                    pillContainer.AddThemeStyleboxOverride("panel", newStyle);
                }
            }
        }
    }
}