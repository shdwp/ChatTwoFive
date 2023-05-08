namespace ChatTwoFive.Ui.SettingsTabs;

internal interface ISettingsTab {
    string Name { get; }
    void Draw(bool changed);
}
