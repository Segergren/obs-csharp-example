So I have had this issue where `monitor_capture` lags a lot when recording. It does not lag when recording through OBS so there must be some type of bug in my application. One thing I can think of is that I'm not loading the modules in the correct order according to the docs: https://docs.obsproject.com/frontends#initialization-and-shutdown

Docs: `obs_startup()` -> `obs_reset_video()` -> `obs_reset_audio()` -> `obs_add_module_path()` -> `obs_load_all_modules()`  -> `obs_post_load_modules()`.

My app: `obs_startup()` -> `obs_add_module_path()` -> `obs_reset_audio()` -> `obs_load_all_modules()` -> `obs_reset_video()` -> `obs_post_load_modules()`

If I change the order in my app so that I run `obs_reset_video()` before `obs_load_all_modules();` it causes `monitor_capture` to have
```
12:49:03:726    [12:49:03 INF] info: [duplicator-monitor-capture: 'display'] update settings:
12:49:03:726        display:  (0x0)
12:49:03:726        cursor: false
12:49:03:726        method: WGC
12:49:03:726        id: 
12:49:03:726        alt_id: 
12:49:03:726        setting_id: DUMMY
12:49:03:726        force SDR: false
12:49:03:726    [12:49:03 INF] debug: source 'display' (monitor_capture) created
```
instead of just logging
```
12:49:03:726    [12:49:03 INF] debug: source 'display' (monitor_capture) created
```

Does anyone have any clue on what can cause this behaviour or any idea on how to fix this. 

# SETUP
1. Start the application once and it will fail
2. Navigate to the `bin\Debug\net8.0` folder (example: `C:\Users\OlleS\source\repos\OBSTest\bin\Debug\net8.0`)
3. Download the .zip file (https://github.com/Segergren/Segra/blob/main/obs.zip) and extract it there (make sure that everything is in the exact same folder)
4. Go to `libobs-sharp/libobs-sharp/obs.cs` and remove row 12 and 14 -> 16 so it just says `public const string importLibrary = @"obs";`
5. Run!
